using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// Merges the seasons and episodes announced by TMDB with those on disk.
/// </summary>
/// <remarks>
/// Episodes are matched by season and episode number only. An episode known to both sides comes out once, carrying
/// both; an episode on disk that TMDB does not list still comes out, marked by its sources; an episode TMDB announces
/// and the disk lacks comes out missing.
/// </remarks>
internal static class SeriesContentMerger
{
    /// <summary>
    /// The widest episode range a single file is trusted to hold: anything wider is a numbering mistake.
    /// </summary>
    internal const int MaxEpisodesPerFile = 10;

    private static readonly IReadOnlyList<CatalogSource> _both = [CatalogSource.Library, CatalogSource.Tmdb];
    private static readonly IReadOnlyList<CatalogSource> _tmdbOnly = [CatalogSource.Tmdb];
    private static readonly IReadOnlyList<CatalogSource> _libraryOnly = [CatalogSource.Library];

    /// <summary>
    /// Merges the season list.
    /// </summary>
    /// <param name="tmdbSeasons">The seasons listed by TMDB.</param>
    /// <param name="local">The content on disk.</param>
    /// <returns>One entry per season number, by number.</returns>
    public static IReadOnlyList<CatalogSeason> MergeSeasons(IReadOnlyList<TmdbModels.SeasonSummary> tmdbSeasons, LocalSeriesContent local)
    {
        var result = new List<CatalogSeason>();
        var known = new HashSet<int>();
        foreach (var season in tmdbSeasons.Where(s => s.SeasonNumber >= 0))
        {
            if (!known.Add(season.SeasonNumber))
            {
                continue;
            }

            var count = CountLocalEpisodes(local.Episodes, season.SeasonNumber);
            result.Add(new CatalogSeason
            {
                SeasonNumber = season.SeasonNumber,
                Name = Text(season.Name) ?? FindFolder(local, season.SeasonNumber)?.Name,
                Overview = Text(season.Overview),
                EpisodeCount = season.EpisodeCount,
                LocalEpisodeCount = count,
                AirDate = TmdbMapper.ParseDate(season.AirDate),
                PosterUrl = TmdbClient.ImageUrl(season.PosterPath, "w500"),
                Availability = count > 0 ? CatalogAvailability.OnDisk : CatalogAvailability.Missing,
                LocalItemId = FindFolder(local, season.SeasonNumber)?.ItemId,
                Sources = count > 0 ? _both : _tmdbOnly
            });
        }

        // A season on disk that TMDB does not list must not vanish from the sheet.
        foreach (var seasonNumber in local.Episodes.Select(e => e.SeasonNumber).Distinct().Where(n => !known.Contains(n)))
        {
            result.Add(new CatalogSeason
            {
                SeasonNumber = seasonNumber,
                Name = FindFolder(local, seasonNumber)?.Name,
                LocalEpisodeCount = CountLocalEpisodes(local.Episodes, seasonNumber),
                Availability = CatalogAvailability.OnDisk,
                LocalItemId = FindFolder(local, seasonNumber)?.ItemId,
                Sources = _libraryOnly
            });
        }

        return result.OrderBy(s => s.SeasonNumber).ToList();
    }

    /// <summary>
    /// Merges the episodes of one season.
    /// </summary>
    /// <param name="seasonNumber">The season number.</param>
    /// <param name="tmdbEpisodes">The episodes announced by TMDB.</param>
    /// <param name="localEpisodes">The episodes on disk, of any season.</param>
    /// <returns>The episodes by number, those without a number last.</returns>
    public static IReadOnlyList<CatalogEpisode> MergeEpisodes(int seasonNumber, IReadOnlyList<TmdbModels.Episode> tmdbEpisodes, IReadOnlyList<LocalEpisodeEntry> localEpisodes)
    {
        var onDisk = localEpisodes.Where(e => e.SeasonNumber == seasonNumber).ToList();

        // Two copies of one episode (two versions, two libraries) still make one episode: the first file wins.
        var byNumber = new Dictionary<int, LocalEpisodeEntry>();
        foreach (var file in onDisk)
        {
            foreach (var number in Numbers(file))
            {
                byNumber.TryAdd(number, file);
            }
        }

        var result = new List<CatalogEpisode>();
        var tmdbNumbers = new HashSet<int>();
        foreach (var episode in tmdbEpisodes.OrderBy(e => e.EpisodeNumber))
        {
            if (episode.EpisodeNumber < 0 || !tmdbNumbers.Add(episode.EpisodeNumber))
            {
                continue;
            }

            var file = byNumber.GetValueOrDefault(episode.EpisodeNumber);
            result.Add(new CatalogEpisode
            {
                SeasonNumber = seasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                Title = Text(episode.Name) ?? Text(file?.Title),
                Overview = Text(episode.Overview) ?? Text(file?.Overview),
                AirDate = TmdbMapper.ParseDate(episode.AirDate) ?? file?.AirDate,
                StillUrl = TmdbClient.ImageUrl(episode.StillPath, "w300"),
                RuntimeMinutes = episode.Runtime is > 0 ? episode.Runtime : file?.RuntimeMinutes,
                Availability = file is null ? CatalogAvailability.Missing : CatalogAvailability.OnDisk,
                LocalItemId = file?.ItemId,
                Sources = file is null ? _tmdbOnly : _both
            });
        }

        // Files TMDB does not account for at all: specials numbered differently, numbering that diverges, unnumbered files.
        var leftoverNumbers = new HashSet<int>();
        foreach (var file in onDisk.Where(f => !Numbers(f).Any(tmdbNumbers.Contains)))
        {
            if (file.EpisodeNumber is int number && !leftoverNumbers.Add(number))
            {
                continue;
            }

            result.Add(new CatalogEpisode
            {
                SeasonNumber = seasonNumber,
                EpisodeNumber = file.EpisodeNumber,
                EpisodeNumberEnd = file.EpisodeNumberEnd > file.EpisodeNumber ? file.EpisodeNumberEnd : null,
                Title = Text(file.Title),
                Overview = Text(file.Overview),
                AirDate = file.AirDate,
                RuntimeMinutes = file.RuntimeMinutes,
                Availability = CatalogAvailability.OnDisk,
                LocalItemId = file.ItemId,
                Sources = _libraryOnly
            });
        }

        return result.OrderBy(e => e.EpisodeNumber ?? int.MaxValue).ToList();
    }

    /// <summary>
    /// Counts the distinct episodes of a season on disk.
    /// </summary>
    /// <param name="localEpisodes">The episodes on disk, of any season.</param>
    /// <param name="seasonNumber">The season number.</param>
    /// <returns>The count; a file holding two episodes counts for two, two copies of one episode for one.</returns>
    public static int CountLocalEpisodes(IReadOnlyList<LocalEpisodeEntry> localEpisodes, int seasonNumber)
    {
        var files = localEpisodes.Where(e => e.SeasonNumber == seasonNumber).ToList();
        return files.SelectMany(Numbers).Distinct().Count() + files.Count(f => f.EpisodeNumber is null);
    }

    /// <summary>
    /// Returns the episode numbers a file holds.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>The numbers, empty when the file has none.</returns>
    internal static IEnumerable<int> Numbers(LocalEpisodeEntry file)
    {
        if (file.EpisodeNumber is not int start)
        {
            return [];
        }

        var end = file.EpisodeNumberEnd is int last && last > start && last - start < MaxEpisodesPerFile ? last : start;
        return Enumerable.Range(start, end - start + 1);
    }

    private static LocalSeasonEntry? FindFolder(LocalSeriesContent local, int seasonNumber)
        => local.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNumber);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
