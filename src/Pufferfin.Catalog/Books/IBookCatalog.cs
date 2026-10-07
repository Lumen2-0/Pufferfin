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

    /// <summary>
    /// Lists the books trending this week, for the Discover screen.
    /// </summary>
    /// <param name="limit">The maximum number of books.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The books, most trending first, each marked <see cref="CatalogAvailability.Missing"/> and carrying its provider and its id in that provider.</returns>
    /// <exception cref="Http.CatalogSourceException">The catalog is not configured (<see cref="CatalogSourceStatus.Disabled"/>) or cannot be reached.</exception>
    Task<IReadOnlyList<CatalogItem>> TrendingAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Gets one book, for its sheet.
    /// </summary>
    /// <param name="provider">The metadata provider of the book, as its catalog entry names it.</param>
    /// <param name="bookId">The id of the book in that provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The book, marked <see cref="CatalogAvailability.Missing"/>, or <c>null</c> when the provider does not know it.</returns>
    /// <exception cref="Http.CatalogSourceException">The catalog is not configured (<see cref="CatalogSourceStatus.Disabled"/>) or cannot be reached.</exception>
    Task<CatalogItem?> GetAsync(string provider, string bookId, CancellationToken cancellationToken);
}
