using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// A film collection (saga).
/// </summary>
public sealed class CatalogCollectionDetails
{
    /// <summary>
    /// Gets the TMDB collection id.
    /// </summary>
    public required int TmdbId { get; init; }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the overview.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the poster URL.
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>
    /// Gets the backdrop URL.
    /// </summary>
    public string? BackdropUrl { get; init; }

    /// <summary>
    /// Gets the films of the collection, in release order, with their availability.
    /// </summary>
    public IReadOnlyList<CatalogItem> Parts { get; init; } = [];
}
