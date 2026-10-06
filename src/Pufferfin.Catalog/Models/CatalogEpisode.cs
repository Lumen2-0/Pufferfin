using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// An episode, TMDB merged with the local library.
/// </summary>
public sealed class CatalogEpisode
{
    /// <summary>
    /// Gets the season number (0 for specials).
    /// </summary>
    public required int SeasonNumber { get; init; }

    /// <summary>
    /// Gets the episode number, or <c>null</c> for a file on disk the library could not number.
    /// </summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>
    /// Gets the last episode number of a file on disk holding several episodes that TMDB does not list.
    /// </summary>
    public int? EpisodeNumberEnd { get; init; }

    /// <summary>
    /// Gets the title, from TMDB or else from the library.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the overview, from TMDB or else from the library.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the air date.
    /// </summary>
    public DateOnly? AirDate { get; init; }

    /// <summary>
    /// Gets the thumbnail URL.
    /// </summary>
    public string? StillUrl { get; init; }

    /// <summary>
    /// Gets the runtime in minutes.
    /// </summary>
    public int? RuntimeMinutes { get; init; }

    /// <summary>
    /// Gets whether the episode is on disk.
    /// </summary>
    public required CatalogAvailability Availability { get; init; }

    /// <summary>
    /// Gets the id of the local library item when the episode is on disk.
    /// </summary>
    /// <remarks>
    /// Two episodes held by one file share the same item.
    /// </remarks>
    public Guid? LocalItemId { get; init; }

    /// <summary>
    /// Gets the sources that know this episode: <see cref="CatalogSource.Library"/> alone marks an episode on disk that TMDB does not list.
    /// </summary>
    public IReadOnlyList<CatalogSource> Sources { get; init; } = [];
}
