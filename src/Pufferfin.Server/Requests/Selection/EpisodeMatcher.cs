using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pufferfin.Server.Arr.Models;

namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// Finds the Sonarr episode of a TMDB episode: by number first, else by air date, one day either side, because
/// time zones shift the dates the sources give and TMDB and TVDB do not always number episodes alike.
/// </summary>
public static class EpisodeMatcher
{
    /// <summary>
    /// The tolerance on the air date, in days.
    /// </summary>
    public const int AirDateToleranceDays = 1;

    /// <summary>
    /// Matches an episode.
    /// </summary>
    /// <param name="episodes">The episodes of the series in Sonarr.</param>
    /// <param name="seasonNumber">The TMDB season number.</param>
    /// <param name="episodeNumber">The TMDB episode number.</param>
    /// <param name="airDate">The TMDB air date, if known.</param>
    /// <returns>The Sonarr episode, or <c>null</c> when nothing matches.</returns>
    public static ArrEpisode? Match(IReadOnlyList<ArrEpisode> episodes, int seasonNumber, int episodeNumber, DateOnly? airDate)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        var byNumber = episodes.FirstOrDefault(e => e.SeasonNumber == seasonNumber && e.EpisodeNumber == episodeNumber);
        if (byNumber is not null || airDate is not DateOnly date)
        {
            return byNumber;
        }

        // Closest date first, then the same season, then the closest episode number.
        return episodes
            .Select(e => (Episode: e, Distance: AirDateDistance(e, date)))
            .Where(c => c.Distance <= AirDateToleranceDays)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Episode.SeasonNumber == seasonNumber ? 0 : 1)
            .ThenBy(c => Math.Abs(c.Episode.EpisodeNumber - episodeNumber))
            .Select(c => c.Episode)
            .FirstOrDefault();
    }

    private static int? AirDateDistance(ArrEpisode episode, DateOnly date)
    {
        if (DateOnly.TryParseExact(episode.AirDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return Math.Abs(local.DayNumber - date.DayNumber);
        }

        return episode.AirDateUtc is { } utc ? Math.Abs(DateOnly.FromDateTime(utc.UtcDateTime).DayNumber - date.DayNumber) : null;
    }
}
