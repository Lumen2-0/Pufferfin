using System;
using System.Collections.Generic;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// A local library item, reduced to what the catalog needs to match it against external sources.
/// </summary>
public sealed class LocalCatalogEntry
{
    /// <summary>
    /// Gets the library item id.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public required CatalogItemKind Kind { get; init; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the original title.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// Gets the production year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the overview.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the TMDB id from the item metadata.
    /// </summary>
    public int? TmdbId { get; init; }

    /// <summary>
    /// Gets the Audible ASIN from the item metadata.
    /// </summary>
    public string? Asin { get; init; }

    /// <summary>
    /// Gets the authors of an audiobook.
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];
}
