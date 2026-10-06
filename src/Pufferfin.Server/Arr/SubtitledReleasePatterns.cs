using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pufferfin.Server.Arr;

/// <summary>
/// Recognizes, from its title, a release that only carries subtitles in a language, not the audio
/// (VOSTFR, "German Subbed", SUBITA...). Asking for French audio and receiving a VOSTFR is a bug.
/// </summary>
/// <remarks>
/// The same patterns feed the custom formats written into Radarr and Sonarr by the auto-configuration and
/// the release selection of the Requests module, so both always agree. They are .NET regular expressions,
/// which is also the dialect of the *arr release title conditions.
/// </remarks>
public sealed class SubtitledReleasePatterns
{
    private const string Separator = "[ ._-]?";

    private static readonly FrozenDictionary<string, string[]> _threeLetterCodes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = ["GER", "DEU"],
        ["en"] = ["ENG"],
        ["es"] = ["SPA", "ESP"],
        ["fr"] = ["FRE", "FRA"],
        ["it"] = ["ITA"],
        ["ja"] = ["JAP", "JPN"],
        ["nl"] = ["DUT", "NLD"],
        ["pt"] = ["POR"],
        ["ru"] = ["RUS"],
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Scene tags that are specific to one language.
    private static readonly FrozenDictionary<string, (string[] Subtitled, string[] Audio)> _sceneTags = new Dictionary<string, (string[], string[])>(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = (["VOSTFR", "VOST", "STFR", "SUBFR"], ["VFF", "VFQ", "VFI", "VF2", "VF", "TRUEFRENCH"]),
        ["de"] = (["GERSUB", "GERSUBBED"], ["DL"]),
        ["es"] = (["VOSE"], ["CASTELLANO", "LATINO"]),
        ["it"] = (["SUBITA"], []),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private SubtitledReleasePatterns(string languageCode, string subtitledPattern, string audioPattern)
    {
        LanguageCode = languageCode;
        SubtitledPattern = subtitledPattern;
        AudioPattern = audioPattern;
        Subtitled = new Regex(subtitledPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Audio = new Regex(audioPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Gets the ISO 639-1 code of the language.
    /// </summary>
    public string LanguageCode { get; }

    /// <summary>
    /// Gets the pattern of a title announcing subtitles in the language.
    /// </summary>
    public string SubtitledPattern { get; }

    /// <summary>
    /// Gets the pattern of a title announcing audio in the language, which wins over the subtitles.
    /// </summary>
    public string AudioPattern { get; }

    private Regex Subtitled { get; }

    private Regex Audio { get; }

    /// <summary>
    /// Builds the patterns of a language.
    /// </summary>
    /// <param name="languageCode">The ISO 639-1 code.</param>
    /// <returns>The patterns, or <c>null</c> when the language is unknown.</returns>
    public static SubtitledReleasePatterns? For(string languageCode)
    {
        ArgumentNullException.ThrowIfNull(languageCode);
        if (ArrLanguages.GetName(languageCode) is not { } name)
        {
            return null;
        }

        var baseCode = languageCode.Split('-')[0];
        var names = new List<string> { Regex.Escape(new string(name.TakeWhile(char.IsLetter).ToArray())) };
        names.AddRange(_threeLetterCodes.GetValueOrDefault(baseCode) ?? []);
        var nameGroup = "(?:" + string.Join('|', names) + ")";
        var (sceneSubtitled, sceneAudio) = _sceneTags.GetValueOrDefault(baseCode, ([], []));

        var subtitled = new List<string>(sceneSubtitled)
        {
            "VOST" + Regex.Escape(baseCode),
            "SUB" + Separator + nameGroup,
            nameGroup + Separator + "SUB(?:S|BED|TITLED)?",
            "SUBBED",
        };

        var audio = new List<string>(sceneAudio)
        {
            "MULTI",
            nameGroup + Separator + "DUB(?:BED)?",
        };

        var subtitledPattern = @"\b(?:" + string.Join('|', subtitled.Distinct(StringComparer.OrdinalIgnoreCase)) + @")\b";

        // The bare language name is audio unless it is glued to a subtitle marker: "FRENCH" is audio, "SUBFRENCH" and "FRENCH.SUBS" are not.
        var audioPattern = @"(?<!SUB" + Separator + @")\b" + nameGroup + @"\b(?!" + Separator + "SUB)|" + @"\b(?:" + string.Join('|', audio) + @")\b";
        return new SubtitledReleasePatterns(baseCode.Equals(languageCode, StringComparison.Ordinal) ? baseCode : languageCode, subtitledPattern, audioPattern);
    }

    /// <summary>
    /// Gets a value indicating whether a title announces subtitles in the language and no audio in it.
    /// </summary>
    /// <param name="title">The release title.</param>
    /// <returns><c>true</c> when the release only has subtitles in the language.</returns>
    public bool IsSubtitledOnly(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return Subtitled.IsMatch(title) && !Audio.IsMatch(title);
    }
}
