using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// The answer of the universal search.
/// </summary>
public sealed class CatalogSearchResponse
{
    /// <summary>
    /// Gets the normalized query.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets the merged entries, one per title or person.
    /// </summary>
    public required IReadOnlyList<CatalogItem> Items { get; init; }

    /// <summary>
    /// Gets the status of every source that was considered.
    /// </summary>
    public required IReadOnlyList<CatalogSourceReport> Sources { get; init; }
}
