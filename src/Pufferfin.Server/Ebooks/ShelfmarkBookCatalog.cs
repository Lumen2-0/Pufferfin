using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Catalog.Books;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// The book catalog of the Discover screen: the metadata provider Shelfmark is configured with for ebooks, through the
/// same Shelfmark client as the audiobooks. Shelfmark not configured: books are off, without an error.
/// </summary>
public sealed class ShelfmarkBookCatalog : IBookCatalog
{
    private readonly IPufferfinConfigurationStore _configuration;
    private readonly ShelfmarkClient _shelfmark;
    private readonly ILogger<ShelfmarkBookCatalog> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShelfmarkBookCatalog"/> class.
    /// </summary>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="shelfmark">The Shelfmark client.</param>
    /// <param name="logger">The logger.</param>
    public ShelfmarkBookCatalog(IPufferfinConfigurationStore configuration, ShelfmarkClient shelfmark, ILogger<ShelfmarkBookCatalog> logger)
    {
        _configuration = configuration;
        _shelfmark = shelfmark;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogItem>> SearchAsync(BookQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_configuration.Current.Settings.GetService(PufferfinService.Shelfmark) is not { } service)
        {
            throw new CatalogSourceException(CatalogSource.Shelfmark, CatalogSourceStatus.Disabled);
        }

        IReadOnlyList<EbookMetadata> books;
        try
        {
            books = await _shelfmark.SearchBooksAsync(service, query.Query, query.Limit, query.Page, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            // The message names the service and the failure only: never a URL nor the key.
            _logger.LogWarning("The book search failed: {Failure}", ex.Message);
            throw new CatalogSourceException(CatalogSource.Shelfmark, Status(ex), ex);
        }

        var language = ShelfmarkBooks.LanguageCode(query.Language);
        return books
            .Where(b => language is null || b.Language is null || b.Language == language)
            .Select(ShelfmarkBooks.ToCatalogItem)
            .Take(query.Limit)
            .ToList();
    }

    private static CatalogSourceStatus Status(ArrApiException ex) => ex.Failure switch
    {
        ArrFailure.KeyRejected => CatalogSourceStatus.Unauthorized,
        ArrFailure.TimedOut => CatalogSourceStatus.TimedOut,
        _ when ex.StatusCode == 429 => CatalogSourceStatus.RateLimited,
        _ => CatalogSourceStatus.Unavailable
    };
}
