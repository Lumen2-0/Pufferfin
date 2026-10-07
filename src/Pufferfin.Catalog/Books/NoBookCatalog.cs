using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Books;

/// <summary>
/// The book catalog of a host without Shelfmark: always off.
/// </summary>
public sealed class NoBookCatalog : IBookCatalog
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogItem>> SearchAsync(BookQuery query, CancellationToken cancellationToken)
        => throw new CatalogSourceException(CatalogSource.Shelfmark, CatalogSourceStatus.Disabled);
}
