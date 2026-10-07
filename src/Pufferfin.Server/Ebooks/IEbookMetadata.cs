using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Reads a book to read from a metadata provider of Shelfmark.
/// </summary>
public interface IEbookMetadata
{
    /// <summary>
    /// Gets a book.
    /// </summary>
    /// <param name="shelfmark">The Shelfmark settings.</param>
    /// <param name="provider">The metadata provider (<c>hardcover</c>, <c>openlibrary</c>, <c>googlebooks</c>).</param>
    /// <param name="bookId">The id of the book in that provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The book, or <c>null</c> when the provider does not know it.</returns>
    /// <exception cref="Arr.ArrApiException">Shelfmark cannot be reached, refused the key, or its provider failed.</exception>
    Task<EbookMetadata?> GetAsync(ServiceSettings shelfmark, string provider, string bookId, CancellationToken cancellationToken);
}
