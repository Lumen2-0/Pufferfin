using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Caching;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Audible;

/// <summary>
/// Audible catalog search across the configured markets (<c>AUDIBLE_MARKETS</c>).
/// </summary>
/// <remarks>
/// The public catalog endpoint needs no account. A market that fails is skipped as long as another one answered.
/// </remarks>
public sealed partial class AudibleCatalogService
{
    /// <summary>
    /// The name of the HTTP client registered for Audible.
    /// </summary>
    public const string HttpClientName = "Pufferfin.Audible";

    private const string ResponseGroups = "contributors,media,product_attrs,product_desc,series";

    // New releases: the pages read at most, and the books already out to gather, per market
    private const int NewReleasesMaxPages = 8;
    private const int NewReleasesWanted = 40;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICatalogCache _cache;
    private readonly CatalogRateLimiters _rateLimiters;
    private readonly ICatalogSettingsProvider _settingsProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudibleCatalogService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="cache">The catalog cache.</param>
    /// <param name="rateLimiters">The outbound rate limiters.</param>
    /// <param name="settingsProvider">The catalog settings.</param>
    public AudibleCatalogService(
        IHttpClientFactory httpClientFactory,
        ICatalogCache cache,
        CatalogRateLimiters rateLimiters,
        ICatalogSettingsProvider settingsProvider)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _rateLimiters = rateLimiters;
        _settingsProvider = settingsProvider;
    }

    /// <summary>
    /// Searches audiobooks in every configured market. The entries are not marked with their availability.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audiobooks, one per ASIN, by relevance.</returns>
    /// <exception cref="CatalogSourceException">Audible is disabled or no market answered.</exception>
    public async Task<IReadOnlyList<CatalogItem>> SearchAsync(AudibleQuery query, CancellationToken cancellationToken)
    {
        var markets = _settingsProvider.GetSettings().AudibleMarkets
            .Where(m => AudibleMarkets.TryGetBaseAddress(m, out _))
            .ToList();
        if (markets.Count == 0)
        {
            throw new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Disabled);
        }

        if (!query.HasCriteria)
        {
            return [];
        }

        // The markets are queried in parallel; awaiting them in order keeps the market priority of the configuration.
        var tasks = markets.Select(m => SearchMarketAsync(m, query, cancellationToken)).ToList();
        var succeeded = new List<IReadOnlyList<CatalogItem>>();
        CatalogSourceException? firstError = null;
        foreach (var task in tasks)
        {
            try
            {
                succeeded.Add(await task.ConfigureAwait(false));
            }
            catch (CatalogSourceException ex)
            {
                firstError ??= ex;
            }
        }

        if (succeeded.Count == 0)
        {
            throw firstError ?? new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Unavailable);
        }

        var language = string.IsNullOrWhiteSpace(query.Language) ? null : AudibleMarkets.ToAudibleLanguage(query.Language);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CatalogItem>();
        var longest = succeeded.Max(r => r.Count);
        for (var rank = 0; rank < longest && result.Count < query.Limit; rank++)
        {
            foreach (var list in succeeded)
            {
                if (rank < list.Count
                    && result.Count < query.Limit
                    && (language is null || string.Equals(list[rank].Language, language, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(query.Series) || (list[rank].SeriesName?.Contains(query.Series, StringComparison.OrdinalIgnoreCase) ?? false))
                    && seen.Add(list[rank].Asin!))
                {
                    result.Add(list[rank]);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Lists the popular audiobooks of every configured market: best sellers or new releases, books only (no podcast,
    /// no adult title), in the preferred audio languages when some are set. The entries are not marked with their availability.
    /// </summary>
    /// <param name="sort">The ranking.</param>
    /// <param name="limit">The maximum number of audiobooks.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audiobooks, one per ASIN, the markets interleaved in their configured order.</returns>
    /// <exception cref="CatalogSourceException">Audible is disabled or no market answered.</exception>
    public async Task<IReadOnlyList<CatalogItem>> PopularAsync(AudiblePopularSort sort, int limit, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.GetSettings();
        var markets = settings.AudibleMarkets
            .Where(m => AudibleMarkets.TryGetBaseAddress(m, out _))
            .ToList();
        if (markets.Count == 0)
        {
            throw new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Disabled);
        }

        var tasks = markets.Select(m => ListMarketAsync(m, sort, cancellationToken)).ToList();
        var succeeded = new List<IReadOnlyList<CatalogItem>>();
        CatalogSourceException? firstError = null;
        foreach (var task in tasks)
        {
            try
            {
                succeeded.Add(await task.ConfigureAwait(false));
            }
            catch (CatalogSourceException ex)
            {
                firstError ??= ex;
            }
        }

        if (succeeded.Count == 0)
        {
            throw firstError ?? new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Unavailable);
        }

        var languages = settings.PreferredAudioLanguages
            .Select(AudibleMarkets.ToAudibleLanguage)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CatalogItem>();
        var longest = succeeded.Max(r => r.Count);
        for (var rank = 0; rank < longest && result.Count < limit; rank++)
        {
            foreach (var list in succeeded)
            {
                if (rank < list.Count
                    && result.Count < limit
                    && (languages.Count == 0 || (list[rank].Language is { } language && languages.Contains(language)))
                    && seen.Add(list[rank].Asin!))
                {
                    result.Add(list[rank]);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets one audiobook by ASIN: from the market it was found in first, then from the other configured markets.
    /// </summary>
    /// <param name="asin">The ASIN.</param>
    /// <param name="market">The market it was found in, when known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audiobook, or <c>null</c> when no market knows it.</returns>
    /// <exception cref="CatalogSourceException">Audible is disabled or no market answered.</exception>
    public async Task<CatalogItem?> GetProductAsync(string asin, string? market, CancellationToken cancellationToken)
        => await FindProductAsync(asin, market, cancellationToken).ConfigureAwait(false) is { } found ? ToItem(found.Product, found.Market) : null;

    /// <summary>
    /// Gets the sheet of an audiobook by ASIN: from the given market first, then from the other configured markets.
    /// The sheet is not marked with its availability.
    /// </summary>
    /// <param name="asin">The ASIN.</param>
    /// <param name="market">The market to read first, when known.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sheet, or <c>null</c> when no market knows the book.</returns>
    /// <exception cref="CatalogSourceException">Audible is disabled or no market answered.</exception>
    public async Task<CatalogAudiobookDetails?> GetDetailsAsync(string asin, string? market, CancellationToken cancellationToken)
        => await FindProductAsync(asin, market, cancellationToken).ConfigureAwait(false) is { } found ? ToDetails(found.Product, found.Market) : null;

    /// <summary>
    /// Converts an Audible product into a sheet.
    /// </summary>
    /// <param name="product">The product.</param>
    /// <param name="market">The market it was read from.</param>
    /// <returns>The sheet, or <c>null</c> when the product has no ASIN or title.</returns>
    internal static CatalogAudiobookDetails? ToDetails(AudibleModels.Product product, string market)
    {
        if (string.IsNullOrWhiteSpace(product.Asin) || string.IsNullOrWhiteSpace(product.Title))
        {
            return null;
        }

        var series = product.Series.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Title));
        product.ProductImages.TryGetValue("500", out var image);
        var released = DateOnly.TryParseExact(product.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateOnly?)null;
        return new CatalogAudiobookDetails
        {
            Asin = product.Asin.ToUpperInvariant(),
            Title = product.Title,
            Subtitle = string.IsNullOrWhiteSpace(product.Subtitle) ? null : product.Subtitle,
            Authors = product.Authors.Select(a => a.Name).OfType<string>().ToList(),
            Narrators = product.Narrators.Select(a => a.Name).OfType<string>().ToList(),
            SeriesName = series?.Title,
            SeriesPosition = string.IsNullOrWhiteSpace(series?.Sequence) ? null : series.Sequence,
            RuntimeMinutes = product.RuntimeLengthMin is > 0 ? product.RuntimeLengthMin : null,
            ImageUrl = image,
            Overview = StripHtml(product.PublisherSummary) ?? StripHtml(product.MerchandisingSummary),
            Language = product.Language?.ToLowerInvariant(),
            LanguageCode = AudibleMarkets.ToLanguageCode(product.Language),
            Market = market,
            IsCompleteEdition = AudiobookTitles.IsCompleteEdition(product.Title, product.Subtitle, product.Series.Select(s => s.Sequence)),
            ReleaseDate = released,
            Year = released?.Year,
            Availability = CatalogAvailability.Missing
        };
    }

    private async Task<(AudibleModels.Product Product, string Market)?> FindProductAsync(string asin, string? market, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        var markets = _settingsProvider.GetSettings().AudibleMarkets
            .Prepend(market ?? string.Empty)
            .Where(m => AudibleMarkets.TryGetBaseAddress(m, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (markets.Count == 0)
        {
            throw new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Disabled);
        }

        CatalogSourceException? firstError = null;
        var answered = false;
        foreach (var code in markets)
        {
            try
            {
                var product = await GetMarketProductAsync(code, asin.Trim().ToUpperInvariant(), cancellationToken).ConfigureAwait(false);
                answered = true;
                if (product is not null && !string.IsNullOrWhiteSpace(product.Asin) && !string.IsNullOrWhiteSpace(product.Title))
                {
                    return (product, code.ToLowerInvariant());
                }
            }
            catch (CatalogSourceException ex)
            {
                firstError ??= ex;
            }
        }

        return answered ? null : throw firstError ?? new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Unavailable);
    }

    private async Task<AudibleModels.Product?> GetMarketProductAsync(string market, string asin, CancellationToken cancellationToken)
    {
        AudibleMarkets.TryGetBaseAddress(market, out var baseAddress);
        var relative = "catalog/products/" + Uri.EscapeDataString(asin) + "?image_sizes=500&response_groups=" + ResponseGroups;
        var marketCode = market.ToLowerInvariant();
        var envelope = await _cache.GetOrCreateAsync(
            "audible:" + marketCode + ":" + relative,
            CatalogCacheDurations.Audible,
            async ct =>
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                var body = await CatalogHttp.GetStringAsync(
                    client,
                    () => new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress!, relative)),
                    CatalogSource.Audible,
                    _rateLimiters.Audible,
                    ct).ConfigureAwait(false);
                if (body is null)
                {
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<AudibleModels.ProductEnvelope>(body, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Unavailable, ex);
                }
            },
            cancellationToken).ConfigureAwait(false);

        return envelope?.Product;
    }

    private async Task<IReadOnlyList<CatalogItem>> SearchMarketAsync(string market, AudibleQuery query, CancellationToken cancellationToken)
    {
        var path = new StringBuilder("catalog/products?num_results=50&products_sort_by=Relevance&image_sizes=500&response_groups=")
            .Append(ResponseGroups);
        AppendParameter(path, "keywords", string.IsNullOrWhiteSpace(query.Keywords) ? query.Series : query.Keywords);
        AppendParameter(path, "title", query.Title);
        AppendParameter(path, "author", query.Author);
        var marketCode = market.ToLowerInvariant();
        var products = await GetProductsAsync(market, path.ToString(), cancellationToken).ConfigureAwait(false);
        return products.Select(p => ToItem(p, marketCode)).OfType<CatalogItem>().ToList();
    }

    private async Task<IReadOnlyList<CatalogItem>> ListMarketAsync(string market, AudiblePopularSort sort, CancellationToken cancellationToken)
    {
        var marketCode = market.ToLowerInvariant();
        if (sort == AudiblePopularSort.BestSellers)
        {
            var bestSellers = await GetProductsAsync(market, PopularPath("BestSellers", 1), cancellationToken).ConfigureAwait(false);
            return bestSellers.Where(IsBook).Select(p => ToItem(p, marketCode)).OfType<CatalogItem>().ToList();
        }

        // Sorted by release date, Audible lists the pre-orders first, often over several pages: the pages are read in
        // turn until enough books already out are found.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var released = new List<CatalogItem>();
        for (var page = 1; page <= NewReleasesMaxPages && released.Count < NewReleasesWanted; page++)
        {
            var products = await GetProductsAsync(market, PopularPath("-ReleaseDate", page), cancellationToken).ConfigureAwait(false);
            if (products.Count == 0)
            {
                break;
            }

            released.AddRange(products
                .Where(IsBook)
                .Where(p => DateOnly.TryParseExact(p.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= today)
                .Select(p => ToItem(p, marketCode))
                .OfType<CatalogItem>());
        }

        return released;
    }

    private static string PopularPath(string sortBy, int page)
        => "catalog/products?num_results=50&page=" + page.ToString(CultureInfo.InvariantCulture)
            + "&products_sort_by=" + sortBy + "&image_sizes=500&response_groups=" + ResponseGroups;

    /// <summary>
    /// Gets a value indicating whether a product is a book to list: neither a podcast or an episode, nor an adult title.
    /// </summary>
    /// <param name="product">The product.</param>
    /// <returns><c>true</c> for a book to list.</returns>
    internal static bool IsBook(AudibleModels.Product product)
        => product.IsAdultProduct != true
            && (product.ContentDeliveryType is null
                || product.ContentDeliveryType.Equals("SinglePartBook", StringComparison.OrdinalIgnoreCase)
                || product.ContentDeliveryType.Equals("MultiPartBook", StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<AudibleModels.Product>> GetProductsAsync(string market, string relative, CancellationToken cancellationToken)
    {
        AudibleMarkets.TryGetBaseAddress(market, out var baseAddress);
        var marketCode = market.ToLowerInvariant();

        var products = await _cache.GetOrCreateAsync(
            "audible:" + marketCode + ":" + relative,
            CatalogCacheDurations.Audible,
            async ct =>
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                var body = await CatalogHttp.GetStringAsync(
                    client,
                    () => new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress!, relative)),
                    CatalogSource.Audible,
                    _rateLimiters.Audible,
                    ct).ConfigureAwait(false);
                if (body is null)
                {
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<AudibleModels.ProductList>(body, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new CatalogSourceException(CatalogSource.Audible, CatalogSourceStatus.Unavailable, ex);
                }
            },
            cancellationToken).ConfigureAwait(false);

        return products?.Products ?? [];
    }

    private static void AppendParameter(StringBuilder path, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            path.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value.Trim().ToLowerInvariant()));
        }
    }

    /// <summary>
    /// Converts an Audible product.
    /// </summary>
    /// <param name="product">The product.</param>
    /// <param name="market">The market it was found in.</param>
    /// <returns>The catalog entry, or <c>null</c> when the product has no ASIN or title.</returns>
    internal static CatalogItem? ToItem(AudibleModels.Product product, string market)
    {
        if (string.IsNullOrWhiteSpace(product.Asin) || string.IsNullOrWhiteSpace(product.Title))
        {
            return null;
        }

        var series = product.Series.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Title));
        product.ProductImages.TryGetValue("500", out var image);
        return new CatalogItem
        {
            Key = "audible:" + product.Asin.ToUpperInvariant(),
            Kind = CatalogItemKind.Audiobook,
            Title = product.Title,
            Year = DateOnly.TryParseExact(product.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.Year : null,
            Overview = StripHtml(product.MerchandisingSummary),
            ImageUrl = image,
            Availability = CatalogAvailability.Missing,
            Asin = product.Asin.ToUpperInvariant(),
            Authors = product.Authors.Select(a => a.Name).OfType<string>().ToList(),
            Narrators = product.Narrators.Select(a => a.Name).OfType<string>().ToList(),
            SeriesName = series is null ? null : (string.IsNullOrWhiteSpace(series.Sequence) ? series.Title : series.Title + ", " + series.Sequence),
            Language = product.Language?.ToLowerInvariant(),
            Market = market,
            IsCompleteEdition = AudiobookTitles.IsCompleteEdition(product.Title, product.Subtitle, product.Series.Select(s => s.Sequence)),
            Sources = [CatalogSource.Audible]
        };
    }

    private static string? StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(HtmlTagRegex().Replace(value, " "));
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
