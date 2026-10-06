using System.Threading;
using System.Threading.Tasks;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Reads an audiobook from the Audible catalog.
/// </summary>
public interface IAudiobookMetadata
{
    /// <summary>
    /// Gets an audiobook by ASIN.
    /// </summary>
    /// <param name="asin">The ASIN.</param>
    /// <param name="market">The Audible market the book was found in, when known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audiobook, or <c>null</c> when Audible does not know it.</returns>
    /// <exception cref="Pufferfin.Catalog.Http.CatalogSourceException">Audible cannot be reached or no market is configured.</exception>
    Task<AudiobookMetadata?> GetAsync(string asin, string? market, CancellationToken cancellationToken);
}
