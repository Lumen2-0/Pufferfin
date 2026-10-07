using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Reads books from the metadata providers of Shelfmark, through the Shelfmark client of the audiobooks.
/// </summary>
public sealed class ShelfmarkEbookMetadata : IEbookMetadata
{
    private readonly ShelfmarkClient _shelfmark;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShelfmarkEbookMetadata"/> class.
    /// </summary>
    /// <param name="shelfmark">The Shelfmark client.</param>
    public ShelfmarkEbookMetadata(ShelfmarkClient shelfmark)
    {
        _shelfmark = shelfmark;
    }

    /// <inheritdoc />
    public Task<EbookMetadata?> GetAsync(ServiceSettings shelfmark, string provider, string bookId, CancellationToken cancellationToken)
        => _shelfmark.GetBookAsync(shelfmark, provider, bookId, cancellationToken);
}
