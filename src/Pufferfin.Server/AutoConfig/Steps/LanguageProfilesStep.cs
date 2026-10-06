using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Creates, in Radarr or Sonarr, one quality profile per preferred audio language and one "Original" profile,
/// with the custom formats they rely on.
/// </summary>
/// <remarks>
/// <para>
/// A language profile only accepts releases carrying the audio of its language: the language custom format scores
/// <see cref="LanguageScore"/> and the profile requires that score. When <c>REJECT_SUBTITLED_ONLY</c> is on (the default),
/// a second custom format recognizes the releases that only carry subtitles in the language (VOSTFR, "Subbed"...) and
/// scores <see cref="SubtitledOnlyScore"/>, which pushes them under the minimum.
/// </para>
/// <para>
/// Qualities are grouped by resolution: 720p and 1080p are allowed, 1080p is the cutoff; 2160p and every Remux are
/// present but not allowed, Remux only at the top, so an administrator can turn them on in the service.
/// Upgrades are off: a file already imported is never downloaded again.
/// </para>
/// <para>
/// Existing items are matched by name and never modified. Changing a setting later only adds what is missing.
/// </para>
/// </remarks>
public sealed class LanguageProfilesStep : IAutoConfigStep
{
    /// <summary>
    /// The score of the language custom format in its profile, which is also the minimum score of the profile.
    /// </summary>
    public const int LanguageScore = 100;

    /// <summary>
    /// The score of the subtitled only custom format in the profile of its language.
    /// </summary>
    public const int SubtitledOnlyScore = -10000;

    /// <summary>
    /// The name of the profile of the original language.
    /// </summary>
    public const string OriginalProfileName = "Pufferfin Original";

    private const string OriginalName = "Original";

    private static readonly (int Resolution, int GroupId, bool Allowed)[] _tiers = [(720, 1001, true), (1080, 1002, true), (2160, 1003, false)];

    private readonly ArrClient _client;
    private readonly PufferfinService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageProfilesStep"/> class.
    /// </summary>
    /// <param name="client">The Radarr and Sonarr client.</param>
    /// <param name="service">Radarr or Sonarr.</param>
    public LanguageProfilesStep(ArrClient client, PufferfinService service)
    {
        if (service is not (PufferfinService.Radarr or PufferfinService.Sonarr))
        {
            throw new ArgumentOutOfRangeException(nameof(service), service, "Only Radarr and Sonarr have quality profiles.");
        }

        _client = client;
        _service = service;
    }

    /// <inheritdoc />
    public string Name => $"{_service} language profiles";

    /// <summary>
    /// Gets the name of the quality profile of a language.
    /// </summary>
    /// <param name="languageName">The *arr language name, or <c>Original</c>.</param>
    /// <returns>The profile name.</returns>
    public static string ProfileName(string languageName) => "Pufferfin " + languageName;

    /// <summary>
    /// Gets the name of the custom format recognizing the audio of a language.
    /// </summary>
    /// <param name="languageName">The *arr language name, or <c>Original</c>.</param>
    /// <returns>The custom format name.</returns>
    public static string LanguageFormatName(string languageName) => "Pufferfin audio " + languageName;

    /// <summary>
    /// Gets the name of the custom format recognizing a release subtitled only in a language.
    /// </summary>
    /// <param name="languageName">The *arr language name.</param>
    /// <returns>The custom format name.</returns>
    public static string SubtitledFormatName(string languageName) => "Pufferfin subtitled only " + languageName;

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(_service) is not { } service)
        {
            return StepOutcomes.NotConfigured(_service.ToString());
        }

        var created = new List<string>();
        var notes = new List<string>();
        try
        {
            var known = await _client.GetLanguagesAsync(service, cancellationToken).ConfigureAwait(false);
            var languages = new List<(string Code, ArrLanguage Language)>();
            foreach (var code in settings.PreferredAudioLanguages)
            {
                if (ArrLanguages.Find(code, known) is { } language)
                {
                    if (!languages.Any(l => l.Language.Id == language.Id))
                    {
                        languages.Add((code, language));
                    }
                }
                else
                {
                    notes.Add($"{_service} does not know the language {code}");
                }
            }

            var rejectSubtitled = settings.RejectSubtitledOnly ?? true;
            var formats = await _client.GetItemsAsync(service, "customformat", cancellationToken).ConfigureAwait(false);
            var formatSchema = await _client.GetSchemaAsync(service, "customformat", cancellationToken).ConfigureAwait(false);

            async Task EnsureFormatAsync(string name, Func<JsonObject?> build)
            {
                if (formats.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                if (build() is not { } format)
                {
                    notes.Add($"{_service} cannot express the custom format {name}");
                    return;
                }

                formats.Add(await _client.CreateItemAsync(service, "customformat", format, cancellationToken).ConfigureAwait(false));
                created.Add("the custom format " + name);
            }

            await EnsureFormatAsync(LanguageFormatName(OriginalName), () => LanguageFormat(formatSchema, OriginalName, ArrLanguages.OriginalId)).ConfigureAwait(false);
            foreach (var (code, language) in languages)
            {
                await EnsureFormatAsync(LanguageFormatName(language.Name!), () => LanguageFormat(formatSchema, language.Name!, language.Id)).ConfigureAwait(false);
                if (rejectSubtitled && SubtitledReleasePatterns.For(code) is { } patterns)
                {
                    await EnsureFormatAsync(SubtitledFormatName(language.Name!), () => SubtitledFormat(formatSchema, language.Name!, patterns)).ConfigureAwait(false);
                }
            }

            var profiles = await _client.GetItemsAsync(service, "qualityprofile", cancellationToken).ConfigureAwait(false);
            var wanted = languages.Select(l => l.Language.Name!).Prepend(OriginalName).ToList();
            foreach (var languageName in wanted)
            {
                var profileName = ProfileName(languageName);
                if (profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // The template lists every custom format, which the service requires: read it after creating ours.
                var template = (await _client.GetSchemaAsync(service, "qualityprofile", cancellationToken).ConfigureAwait(false)).AsObject();
                var profile = BuildProfile(template, profileName, formats, LanguageFormatName(languageName), languageName == OriginalName ? null : SubtitledFormatName(languageName));
                profiles.Add(await _client.CreateItemAsync(service, "qualityprofile", profile, cancellationToken).ConfigureAwait(false));
                created.Add("the quality profile " + profileName);
            }
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex, created);
        }

        var outcome = StepOutcomes.FromCreated(created);
        return notes.Count == 0 ? outcome : outcome with { Detail = string.Join(" ", new[] { outcome.Detail, string.Join("; ", notes) + "." }.Where(d => d is not null)) };
    }

    /// <summary>
    /// Builds a quality profile from the service template.
    /// </summary>
    /// <param name="template">The quality profile template.</param>
    /// <param name="name">The profile name.</param>
    /// <param name="formats">Every custom format of the service.</param>
    /// <param name="languageFormat">The name of the language custom format the profile requires.</param>
    /// <param name="subtitledFormat">The name of the subtitled only custom format the profile refuses, if any.</param>
    /// <returns>The profile.</returns>
    internal static JsonObject BuildProfile(JsonObject template, string name, IReadOnlyList<ArrNamedItem> formats, string languageFormat, string? subtitledFormat)
    {
        var profile = template.DeepClone().AsObject();
        profile.Remove("id");
        profile["name"] = name;
        profile["upgradeAllowed"] = false;
        profile["minFormatScore"] = LanguageScore;
        profile["cutoffFormatScore"] = LanguageScore;
        if (profile.ContainsKey("language"))
        {
            // Radarr also filters on the profile language: the custom formats decide instead.
            profile["language"] = new JsonObject { ["id"] = ArrLanguages.AnyId, ["name"] = "Any" };
        }

        var qualities = Flatten(profile["items"] as JsonArray).ToList();
        var items = new JsonArray();
        bool IsRemux(JsonObject quality) => quality["name"]?.GetValue<string>()?.Contains("Remux", StringComparison.OrdinalIgnoreCase) == true;
        int Resolution(JsonObject quality) => quality["resolution"]?.GetValue<int>() ?? 0;

        // Lowest priority first: the service prefers the last allowed item.
        foreach (var quality in qualities.Where(q => !IsRemux(q) && !_tiers.Any(t => t.Resolution == Resolution(q))))
        {
            items.Add(Item(quality, false));
        }

        foreach (var quality in qualities.Where(q => IsRemux(q) && Resolution(q) != _tiers[^1].Resolution))
        {
            items.Add(Item(quality, false));
        }

        foreach (var (resolution, groupId, allowed) in _tiers)
        {
            var members = qualities.Where(q => !IsRemux(q) && Resolution(q) == resolution).ToList();
            if (members.Count > 0)
            {
                items.Add(new JsonObject
                {
                    ["id"] = groupId,
                    ["name"] = resolution + "p",
                    ["allowed"] = allowed,
                    ["items"] = new JsonArray(members.Select(q => (JsonNode)Item(q, allowed)).ToArray())
                });
            }
        }

        foreach (var quality in qualities.Where(q => IsRemux(q) && Resolution(q) == _tiers[^1].Resolution))
        {
            items.Add(Item(quality, false));
        }

        profile["items"] = items;
        profile["cutoff"] = _tiers.First(t => t.Resolution == 1080).GroupId;

        var formatItems = new JsonArray();
        foreach (var format in formats)
        {
            var score = string.Equals(format.Name, languageFormat, StringComparison.OrdinalIgnoreCase) ? LanguageScore
                : subtitledFormat is not null && string.Equals(format.Name, subtitledFormat, StringComparison.OrdinalIgnoreCase) ? SubtitledOnlyScore
                : 0;
            formatItems.Add(new JsonObject { ["format"] = format.Id, ["name"] = format.Name, ["score"] = score });
        }

        profile["formatItems"] = formatItems;
        return profile;
    }

    private static JsonObject Item(JsonObject quality, bool allowed)
        => new() { ["quality"] = quality.DeepClone(), ["items"] = new JsonArray(), ["allowed"] = allowed };

    private static IEnumerable<JsonObject> Flatten(JsonArray? items)
    {
        foreach (var item in items?.OfType<JsonObject>() ?? [])
        {
            if (item["quality"] is JsonObject quality)
            {
                yield return quality;
            }

            foreach (var nested in Flatten(item["items"] as JsonArray))
            {
                yield return nested;
            }
        }
    }

    private static JsonObject? LanguageFormat(JsonNode? schema, string languageName, int languageId)
    {
        if (Condition(schema, "LanguageSpecification", "Language " + languageName, negate: false, value: languageId) is not { } condition)
        {
            return null;
        }

        return Format(LanguageFormatName(languageName), condition);
    }

    private static JsonObject? SubtitledFormat(JsonNode? schema, string languageName, SubtitledReleasePatterns patterns)
    {
        if (Condition(schema, "ReleaseTitleSpecification", "Subtitles in " + languageName, negate: false, value: patterns.SubtitledPattern) is not { } subtitled
            || Condition(schema, "ReleaseTitleSpecification", "No audio in " + languageName, negate: true, value: patterns.AudioPattern) is not { } noAudio)
        {
            return null;
        }

        return Format(SubtitledFormatName(languageName), subtitled, noAudio);
    }

    private static JsonObject Format(string name, params JsonObject[] conditions)
        => new()
        {
            ["name"] = name,
            ["includeCustomFormatWhenRenaming"] = false,
            ["specifications"] = new JsonArray(conditions.Select(c => (JsonNode)c).ToArray())
        };

    private static JsonObject? Condition(JsonNode? schema, string implementation, string name, bool negate, JsonNode value)
    {
        if (ArrFields.Template(schema, implementation) is not { } template || !ArrFields.Set(template, "value", value))
        {
            return null;
        }

        var fields = new JsonArray();
        foreach (var field in (template["fields"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            fields.Add(new JsonObject { ["name"] = field["name"]?.DeepClone(), ["value"] = field["value"]?.DeepClone() });
        }

        return new JsonObject
        {
            ["name"] = name,
            ["implementation"] = implementation,
            ["negate"] = negate,
            ["required"] = true,
            ["fields"] = fields
        };
    }
}
