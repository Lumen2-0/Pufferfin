using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// A season of a series, TMDB merged with the local library.
/// </summary>
public sealed class CatalogSeason
{
    /// <summary>
    /// Gets the season number (0 for specials).
    /// </summary>
    public required int SeasonNumber { get; init; }

    /// <summary>
    /// Gets the name, from TMDB or else from the library.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the overview.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the number of episodes announced by TMDB, or <c>null</c> for a season TMDB does not know.
    /// </summary>
    public int? EpisodeCount { get; init; }

    /// <summary>
    /// Gets the number of distinct episodes on disk. A file holding two episodes counts for two.
    /// </summary>
    public required int LocalEpisodeCount { get; init; }

    /// <summary>
    /// Gets the date of the first episode.
    /// </summary>
    public DateOnly? AirDate { get; init; }

    /// <summary>
    /// Gets the poster URL.
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>
    /// Gets whether at least one episode of the season is on disk.
    /// </summary>
    public required CatalogAvailability Availability { get; init; }

    /// <summary>
    /// Gets the id of the local library item of the season when its folder exists.
    /// </summary>
    public Guid? LocalItemId { get; init; }

    /// <summary>
    /// Gets the sources that know this season: <see cref="CatalogSource.Library"/> alone marks a season on disk that TMDB does not list.
    /// </summary>
    public IReadOnlyList<CatalogSource> Sources { get; init; } = [];
}
