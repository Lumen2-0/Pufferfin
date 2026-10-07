using System;

namespace Pufferfin.Catalog.Caching;

/// <summary>
/// How long each kind of catalog answer is kept.
/// </summary>
public static class CatalogCacheDurations
{
    /// <summary>
    /// Search results: short enough to pick up new releases the same day.
    /// </summary>
    public static readonly TimeSpan Search = TimeSpan.FromHours(1);

    /// <summary>
    /// The weekly trending lists, which TMDB recomputes once a day.
    /// </summary>
    public static readonly TimeSpan Trending = TimeSpan.FromHours(3);

    /// <summary>
    /// The discover lists by genre or series type, ordered by a popularity TMDB recomputes once a day.
    /// </summary>
    public static readonly TimeSpan Discover = TimeSpan.FromHours(3);

    /// <summary>
    /// Detailed sheets of films, series, people and collections.
    /// </summary>
    public static readonly TimeSpan Details = TimeSpan.FromHours(24);

    /// <summary>
    /// Genre lists, which almost never change.
    /// </summary>
    public static readonly TimeSpan Genres = TimeSpan.FromDays(7);

    /// <summary>
    /// Audible catalog searches.
    /// </summary>
    public static readonly TimeSpan Audible = TimeSpan.FromHours(6);

    /// <summary>
    /// The Letterboxd diary feed, refreshed often so a film just watched leaves the recommendations quickly.
    /// </summary>
    public static readonly TimeSpan Letterboxd = TimeSpan.FromMinutes(30);
}
