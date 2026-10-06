using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// Read access to the local library, filtered by what the user is allowed to see.
/// </summary>
public interface ILocalCatalogIndex
{
    /// <summary>
    /// Searches the library.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="userId">The user the results are filtered for.</param>
    /// <param name="kinds">The kinds to search.</param>
    /// <param name="limit">The maximum number of results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching items, by relevance.</returns>
    Task<IReadOnlyList<LocalCatalogEntry>> SearchAsync(string query, Guid userId, IReadOnlyCollection<CatalogItemKind> kinds, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the local items carrying the given TMDB ids.
    /// </summary>
    /// <param name="kind">The kind of the items.</param>
    /// <param name="tmdbIds">The TMDB ids.</param>
    /// <param name="userId">The user the results are filtered for.</param>
    /// <returns>The local items by TMDB id.</returns>
    IReadOnlyDictionary<int, LocalCatalogEntry> FindByTmdbIds(CatalogItemKind kind, IReadOnlyCollection<int> tmdbIds, Guid userId);

    /// <summary>
    /// Returns the TMDB ids of the films the user has already played.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The TMDB ids.</returns>
    IReadOnlySet<int> GetPlayedMovieTmdbIds(Guid userId);
}
