using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;

namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// Picks the release to grab among the results of an interactive search: the audio languages are tried in order,
/// the original version last, and a single episode is never taken from a season pack.
/// </summary>
/// <remarks>
/// Releases keep the order the service ranked them in (quality, then its own preferences). A release the service
/// rejected is skipped, unless every reason only concerns the language or the custom format score: the quality
/// profile of the title may belong to another language than the one being tried. A torrent without seeders is skipped.
/// </remarks>
public static class ReleaseSelector
{
    /// <summary>
    /// Selects a release.
    /// </summary>
    /// <param name="releases">The search results, in the order the service ranked them.</param>
    /// <param name="plan">The languages to try, in order, ending with the original version.</param>
    /// <param name="originalLanguage">The ISO 639-1 original language of the title, if known.</param>
    /// <param name="rejectSubtitledOnly">Whether a release that only has subtitles in the wanted language is refused.</param>
    /// <param name="scope">What the release must cover.</param>
    /// <returns>The selection: a release and its language, or the problem.</returns>
    public static ReleaseSelection Select(
        IReadOnlyList<ArrRelease> releases,
        IReadOnlyList<LanguageCandidate> plan,
        string? originalLanguage,
        bool rejectSubtitledOnly,
        SelectionScope scope)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(scope);

        var usable = releases.Where(r => !IsHardRejected(r) && !IsDead(r)).ToList();
        var inScope = usable.Where(r => InScope(r, scope)).ToList();

        foreach (var candidate in plan)
        {
            var release = inScope.FirstOrDefault(r => HasAudio(r, candidate, originalLanguage, rejectSubtitledOnly));
            if (release is not null)
            {
                return new ReleaseSelection(release, candidate, null);
            }
        }

        if (inScope.Count == 0)
        {
            if (scope.Mode == SelectionMode.Episode && usable.Any(r => r.FullSeason || r.EpisodeNumbers.Count > 1))
            {
                return new ReleaseSelection(null, null, new RequestProblem(
                    RequestProblemCode.OnlySeasonPacks,
                    "Only season packs or multi-episode releases were found. Pufferfin only downloads the episode asked for."));
            }

            return new ReleaseSelection(null, null, new RequestProblem(RequestProblemCode.NoRelease, "No release was found yet."));
        }

        var seen = inScope
            .SelectMany(r => r.Languages)
            .Select(l => l.Name)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var tried = string.Join(", ", plan.Select(c => c.IsOriginal ? "the original version" : ArrLanguages.GetName(c.Code) ?? c.Code));
        return new ReleaseSelection(null, null, new RequestProblem(
            RequestProblemCode.NoAcceptableLanguage,
            "Releases were found, but none in " + tried + ".",
            seen.Count == 0 ? null : "Languages found: " + string.Join(", ", seen) + "."));
    }

    /// <summary>
    /// Gets a value indicating whether a release carries the audio of a language candidate.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <param name="candidate">The language candidate.</param>
    /// <param name="originalLanguage">The ISO 639-1 original language of the title, if known.</param>
    /// <param name="rejectSubtitledOnly">Whether a release that only has subtitles in the language does not count.</param>
    /// <returns><c>true</c> when the release has the audio.</returns>
    public static bool HasAudio(ArrRelease release, LanguageCandidate candidate, string? originalLanguage, bool rejectSubtitledOnly)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.IsOriginal)
        {
            // Radarr and Sonarr assume the original language when a title names none.
            return originalLanguage is null
                || release.Languages.Count == 0
                || release.Languages.Any(l => l.Id == ArrLanguages.OriginalId || ArrLanguages.Matches(l, originalLanguage));
        }

        if (!release.Languages.Any(l => ArrLanguages.Matches(l, candidate.Code)))
        {
            return false;
        }

        // Subtitles do not matter when the wanted language is the original one: the audio is there anyway.
        var isOriginal = originalLanguage is not null && string.Equals(candidate.Code.Split('-')[0], originalLanguage, StringComparison.OrdinalIgnoreCase);
        return !rejectSubtitledOnly
            || isOriginal
            || SubtitledReleasePatterns.For(candidate.Code)?.IsSubtitledOnly(release.Title ?? string.Empty) != true;
    }

    private static bool InScope(ArrRelease release, SelectionScope scope) => scope.Mode switch
    {
        // Exactly this episode: a pack, or a range such as S01E01-E10, is the season under another name.
        SelectionMode.Episode => !release.FullSeason
            && release.SeasonNumber == scope.SeasonNumber
            && release.EpisodeNumbers.Count == 1
            && release.EpisodeNumbers[0] == scope.EpisodeNumber,
        SelectionMode.SeasonPack => release.FullSeason && release.SeasonNumber == scope.SeasonNumber,
        _ => true
    };

    private static bool IsHardRejected(ArrRelease release)
        => release.Rejected && release.Rejections.Any(reason =>
            !reason.Contains("custom format", StringComparison.OrdinalIgnoreCase)
            && !reason.Contains("language", StringComparison.OrdinalIgnoreCase));

    private static bool IsDead(ArrRelease release)
        => string.Equals(release.Protocol, "torrent", StringComparison.OrdinalIgnoreCase) && release.Seeders == 0;
}
