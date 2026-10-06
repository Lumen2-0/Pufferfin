using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Letterboxd;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tmdb;

namespace Pufferfin.Catalog.Recommendations;

/// <summary>
/// Feeds the "Recommended for you" row of a user from their own Letterboxd diary, when they linked an account.
/// </summary>
/// <remarks>
/// The best rated and most recent films of the diary are the seeds; their TMDB recommendations are scored by how many
/// seeds suggest them and how high. Every film already seen, on Letterboxd or played in Pufferfin by the user, is excluded.
/// </remarks>
public sealed class RecommendationService
{
    /// <summary>
    /// The number of diary films used as seeds.
    /// </summary>
    internal const int SeedCount = 8;

    /// <summary>
    /// Films rated below this (out of 5) are not used as seeds.
    /// </summary>
    internal const double MinSeedRating = 3.0;

    private readonly LetterboxdClient _letterboxd;
    private readonly TmdbCatalogService _tmdb;
    private readonly ILocalCatalogIndex _index;
    private readonly LocalAvailability _availability;
    private readonly ICatalogSettingsProvider _settingsProvider;
    private readonly ILetterboxdAccountProvider _accounts;
    private readonly ILogger<RecommendationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecommendationService"/> class.
    /// </summary>
    /// <param name="letterboxd">The Letterboxd client.</param>
    /// <param name="tmdb">The TMDB catalog.</param>
    /// <param name="index">The local library index.</param>
    /// <param name="availability">The local availability lookup.</param>
    /// <param name="settingsProvider">The catalog settings.</param>
    /// <param name="accounts">The Letterboxd account of each user.</param>
    /// <param name="logger">The logger.</param>
    public RecommendationService(
        LetterboxdClient letterboxd,
        TmdbCatalogService tmdb,
        ILocalCatalogIndex index,
        LocalAvailability availability,
        ICatalogSettingsProvider settingsProvider,
        ILetterboxdAccountProvider accounts,
        ILogger<RecommendationService> logger)
    {
        _letterboxd = letterboxd;
        _tmdb = tmdb;
        _index = index;
        _availability = availability;
        _settingsProvider = settingsProvider;
        _accounts = accounts;
        _logger = logger;
    }

    /// <summary>
    /// Gets the films recommended to a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="limit">The maximum number of films.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The recommendations, best first, with the status of Letterboxd and TMDB.</returns>
    public async Task<CatalogListResponse> GetForYouAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.GetSettings();
        var username = _accounts.GetUsername(userId);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Empty(new CatalogSourceReport(CatalogSource.Letterboxd, CatalogSourceStatus.Disabled));
        }

        if (!settings.IsTmdbEnabled)
        {
            return Empty(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Disabled));
        }

        IReadOnlyList<LetterboxdDiaryEntry> diary;
        try
        {
            diary = await _letterboxd.GetDiaryAsync(username, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogSourceException ex)
        {
            _logger.LogWarning("Letterboxd diary unavailable: {Status}", ex.Status);
            return Empty(new CatalogSourceReport(CatalogSource.Letterboxd, ex.Status));
        }

        var seen = diary.Select(e => e.TmdbId).ToHashSet();
        seen.UnionWith(_index.GetPlayedMovieTmdbIds(userId));

        var seeds = SelectSeeds(diary);
        var tasks = seeds.Select(s => _tmdb.GetMovieRecommendationsAsync(s.TmdbId, cancellationToken)).ToList();
        var lists = new List<IReadOnlyList<CatalogItem>>();
        CatalogSourceException? firstError = null;
        foreach (var task in tasks)
        {
            try
            {
                lists.Add(await task.ConfigureAwait(false));
            }
            catch (CatalogSourceException ex)
            {
                firstError ??= ex;
            }
        }

        var letterboxdReport = new CatalogSourceReport(CatalogSource.Letterboxd, CatalogSourceStatus.Ok);
        if (lists.Count == 0 && firstError is not null)
        {
            return Empty(letterboxdReport, new CatalogSourceReport(CatalogSource.Tmdb, firstError.Status));
        }

        var ranked = Rank(lists, seen).Take(limit).ToList();
        return new CatalogListResponse
        {
            Items = _availability.Annotate(ranked, userId),
            Sources = [letterboxdReport, new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Ok)]
        };
    }

    /// <summary>
    /// Picks the seeds: rated films first, best rated then most recent, excluding films rated below <see cref="MinSeedRating"/>.
    /// </summary>
    /// <param name="diary">The diary.</param>
    /// <returns>The seeds.</returns>
    internal static IReadOnlyList<LetterboxdDiaryEntry> SelectSeeds(IReadOnlyList<LetterboxdDiaryEntry> diary)
        => diary
            .Where(e => e.Rating is null || e.Rating >= MinSeedRating)
            .OrderByDescending(e => e.Rating ?? MinSeedRating)
            .ThenByDescending(e => e.WatchedDate ?? DateOnly.MinValue)
            .DistinctBy(e => e.TmdbId)
            .Take(SeedCount)
            .ToList();

    /// <summary>
    /// Merges the recommendation lists of the seeds and removes the films already seen.
    /// </summary>
    /// <param name="lists">One recommendation list per seed, by relevance.</param>
    /// <param name="seen">The TMDB ids of the films already seen.</param>
    /// <returns>The films, best first.</returns>
    internal static IEnumerable<CatalogItem> Rank(IReadOnlyList<IReadOnlyList<CatalogItem>> lists, IReadOnlySet<int> seen)
    {
        var scores = new Dictionary<int, (CatalogItem Item, double Score, int FirstSeen)>();
        var order = 0;
        foreach (var list in lists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var item = list[rank];
                if (item.TmdbId is not int id || seen.Contains(id))
                {
                    continue;
                }

                // Each seed adds 1 point, plus up to 1 point for the position in its list.
                var points = 1.0 + (1.0 / (rank + 1));
                scores[id] = scores.TryGetValue(id, out var current)
                    ? (current.Item, current.Score + points, current.FirstSeen)
                    : (item, points, order++);
            }
        }

        return scores.Values
            .OrderByDescending(v => v.Score)
            .ThenBy(v => v.FirstSeen)
            .Select(v => v.Item);
    }

    private static CatalogListResponse Empty(params CatalogSourceReport[] sources)
        => new() { Items = [], Sources = sources };
}
