using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// The seasons of a series, TMDB merged with the local library.
/// </summary>
public sealed class CatalogSeriesSeasons
{
    /// <summary>
    /// Gets the TMDB id of the series.
    /// </summary>
    public required int TmdbId { get; init; }

    /// <summary>
    /// Gets whether the series is on disk.
    /// </summary>
    public required CatalogAvailability Availability { get; init; }

    /// <summary>
    /// Gets the id of the local library item of the series when it is on disk.
    /// </summary>
    public Guid? LocalItemId { get; init; }

    /// <summary>
    /// Gets the seasons, by number, specials (season 0) first. One entry per number.
    /// </summary>
    public IReadOnlyList<CatalogSeason> Seasons { get; init; } = [];
}
