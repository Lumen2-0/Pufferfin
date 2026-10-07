using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Catalog.Books;
using Pufferfin.Catalog.Caching;
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
    private const string TrendingCacheKey = "openlibrary:trending:weekly";
    private const int TrendingFetchSize = 60;

    private readonly IPufferfinConfigurationStore _configuration;
    private readonly ShelfmarkClient _shelfmark;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICatalogCache _cache;
    private readonly ILogger<ShelfmarkBookCatalog> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShelfmarkBookCatalog"/> class.
    /// </summary>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="shelfmark">The Shelfmark client.</param>
    /// <param name="httpClientFactory">The HTTP client factory, for Open Library.</param>
    /// <param name="cache">The catalog cache.</param>
    /// <param name="logger">The logger.</param>
    public ShelfmarkBookCatalog(
        IPufferfinConfigurationStore configuration,
        ShelfmarkClient shelfmark,
        IHttpClientFactory httpClientFactory,
        ICatalogCache cache,
        ILogger<ShelfmarkBookCatalog> logger)
    {
        _configuration = configuration;
        _shelfmark = shelfmark;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>The weekly trending books of Open Library (see <see cref="OpenLibraryTrending"/>); books are off without Shelfmark.</remarks>
    public async Task<IReadOnlyList<CatalogItem>> TrendingAsync(int limit, CancellationToken cancellationToken)
    {
        if (_configuration.Current.Settings.GetService(PufferfinService.Shelfmark) is null)
        {
            throw new CatalogSourceException(CatalogSource.Shelfmark, CatalogSourceStatus.Disabled);
        }

        List<EbookMetadata>? books;
        try
        {
            books = await _cache.GetOrCreateAsync(
                TrendingCacheKey,
                CatalogCacheDurations.Trending,
                async ct =>
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(15);
                    return (await OpenLibraryTrending.FetchAsync(client, TrendingFetchSize, ct).ConfigureAwait(false)).ToList();
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("The trending books of Open Library could not be read: {Failure}", ex.Message);
            throw new CatalogSourceException(CatalogSource.Shelfmark, CatalogSourceStatus.Unavailable, ex);
        }

        return (books ?? []).Select(ShelfmarkBooks.ToCatalogItem).Take(limit).ToList();
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

    /// <inheritdoc />
    public async Task<CatalogItem?> GetAsync(string provider, string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        if (_configuration.Current.Settings.GetService(PufferfinService.Shelfmark) is not { } service)
        {
            throw new CatalogSourceException(CatalogSource.Shelfmark, CatalogSourceStatus.Disabled);
        }

        try
        {
            var book = await _shelfmark.GetBookAsync(service, provider, bookId, cancellationToken).ConfigureAwait(false);
            return book is null ? null : ShelfmarkBooks.ToCatalogItem(book);
        }
        catch (ArrApiException ex) when (ex.Failure == ArrFailure.NotFound)
        {
            return null;
        }
        catch (ArrApiException ex)
        {
            _logger.LogWarning("The book lookup failed: {Failure}", ex.Message);
            throw new CatalogSourceException(CatalogSource.Shelfmark, Status(ex), ex);
        }
    }

    private static CatalogSourceStatus Status(ArrApiException ex) => ex.Failure switch
    {
        ArrFailure.KeyRejected => CatalogSourceStatus.Unauthorized,
        ArrFailure.TimedOut => CatalogSourceStatus.TimedOut,
        _ when ex.StatusCode == 429 => CatalogSourceStatus.RateLimited,
        _ => CatalogSourceStatus.Unavailable
    };
}
