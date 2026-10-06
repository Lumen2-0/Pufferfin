using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Search;

namespace Pufferfin.Catalog.Tests.TestSupport;

/// <summary>
/// An in-memory library.
/// </summary>
public sealed class FakeLocalIndex : ILocalCatalogIndex
{
    public Collection<LocalCatalogEntry> Items { get; } = [];

    public HashSet<int> PlayedMovies { get; } = [];

    /// <summary>
    /// Gets the content of each series as each user sees it: a user absent from the key sees nothing.
    /// </summary>
    public Dictionary<(int TmdbId, Guid UserId), LocalSeriesContent> Series { get; } = [];

    public ConcurrentQueue<Guid> SeriesContentRequests { get; } = new();

    public Exception? SearchFailure { get; set; }

    public Task<IReadOnlyList<LocalCatalogEntry>> SearchAsync(string query, Guid userId, IReadOnlyCollection<CatalogItemKind> kinds, int limit, CancellationToken cancellationToken)
    {
        if (SearchFailure is not null)
        {
            throw SearchFailure;
        }

        var normalized = CatalogMerger.Normalize(query);
        IReadOnlyList<LocalCatalogEntry> result = Items
            .Where(i => kinds.Contains(i.Kind) && CatalogMerger.Normalize(i.Title).Contains(normalized, StringComparison.Ordinal))
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public IReadOnlyDictionary<int, LocalCatalogEntry> FindByTmdbIds(CatalogItemKind kind, IReadOnlyCollection<int> tmdbIds, Guid userId)
    {
        var result = new Dictionary<int, LocalCatalogEntry>();
        foreach (var item in Items.Where(i => i.Kind == kind && i.TmdbId is int id && tmdbIds.Contains(id)))
        {
            result.TryAdd(item.TmdbId!.Value, item);
        }

        return result;
    }

    public LocalSeriesContent GetSeriesContent(int seriesTmdbId, Guid userId)
    {
        SeriesContentRequests.Enqueue(userId);
        return Series.TryGetValue((seriesTmdbId, userId), out var content) ? content : LocalSeriesContent.Empty;
    }

    public IReadOnlySet<int> GetPlayedMovieTmdbIds(Guid userId) => PlayedMovies;
}
