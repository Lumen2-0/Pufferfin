using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// Marks TMDB entries that are already in the local library.
/// </summary>
public sealed class LocalAvailability
{
    private readonly ILocalCatalogIndex _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalAvailability"/> class.
    /// </summary>
    /// <param name="index">The local library index.</param>
    public LocalAvailability(ILocalCatalogIndex index)
    {
        _index = index;
    }

    /// <summary>
    /// Returns the entries with their availability set, in the same order.
    /// </summary>
    /// <param name="items">The entries, usually from TMDB.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <returns>The annotated entries.</returns>
    public IReadOnlyList<CatalogItem> Annotate(IReadOnlyList<CatalogItem> items, Guid userId)
    {
        var lookups = items
            .Where(i => i.TmdbId is not null)
            .GroupBy(i => i.Kind)
            .ToDictionary(g => g.Key, g => _index.FindByTmdbIds(g.Key, g.Select(i => i.TmdbId!.Value).Distinct().ToList(), userId));

        return items
            .Select(item => item.TmdbId is int tmdbId
                && lookups.TryGetValue(item.Kind, out var lookup)
                && lookup.TryGetValue(tmdbId, out var local)
                    ? MarkOnDisk(item, local)
                    : item)
            .ToList();
    }

    /// <summary>
    /// Finds the local item of one TMDB title or person.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <returns>The local item, or <c>null</c>.</returns>
    public LocalCatalogEntry? Find(CatalogItemKind kind, int tmdbId, Guid userId)
        => _index.FindByTmdbIds(kind, [tmdbId], userId).TryGetValue(tmdbId, out var local) ? local : null;

    /// <summary>
    /// Marks one entry as present on disk.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <param name="local">The local item.</param>
    /// <returns>The updated entry.</returns>
    public static CatalogItem MarkOnDisk(CatalogItem item, LocalCatalogEntry local)
        => item with
        {
            Availability = CatalogAvailability.OnDisk,
            LocalItemId = local.ItemId,
            Sources = item.Sources.Contains(CatalogSource.Library) ? item.Sources : [CatalogSource.Library, .. item.Sources]
        };
}
