using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// A list of catalog entries with the status of the sources that produced it.
/// </summary>
public sealed class CatalogListResponse
{
    /// <summary>
    /// Gets the entries.
    /// </summary>
    public required IReadOnlyList<CatalogItem> Items { get; init; }

    /// <summary>
    /// Gets the status of every source that was considered.
    /// </summary>
    public required IReadOnlyList<CatalogSourceReport> Sources { get; init; }
}
