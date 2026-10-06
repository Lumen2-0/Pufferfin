using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// A season of a series with its episodes, TMDB merged with the local library.
/// </summary>
public sealed class CatalogSeasonDetails
{
    /// <summary>
    /// Gets the TMDB id of the series.
    /// </summary>
    public required int SeriesTmdbId { get; init; }

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

    /// <summary>
    /// Gets the episodes by number, episodes without a number last. An episode known to TMDB and on disk appears once.
    /// </summary>
    public IReadOnlyList<CatalogEpisode> Episodes { get; init; } = [];
}
