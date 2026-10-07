using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Books;

/// <summary>
/// Searches books to read in an external catalog. The Pufferfin server implements it with the metadata providers of
/// Shelfmark; without it, books are off.
/// </summary>
public interface IBookCatalog
{
    /// <summary>
    /// Searches books.
    /// </summary>
    /// <param name="query">The search.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The books, by relevance, each marked <see cref="CatalogAvailability.Missing"/> and carrying its provider and its id in that provider.</returns>
    /// <exception cref="Http.CatalogSourceException">The catalog is not configured (<see cref="CatalogSourceStatus.Disabled"/>), cannot be reached, or refused the key.</exception>
    Task<IReadOnlyList<CatalogItem>> SearchAsync(BookQuery query, CancellationToken cancellationToken);
}
