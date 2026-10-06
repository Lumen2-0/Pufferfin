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

    private async Task<IReadOnlyList<CatalogItem>> SearchMarketAsync(string market, AudibleQuery query, CancellationToken cancellationToken)
    {
        AudibleMarkets.TryGetBaseAddress(market, out var baseAddress);
        var path = new StringBuilder("catalog/products?num_results=50&products_sort_by=Relevance&image_sizes=500&response_groups=")
            .Append(ResponseGroups);
        AppendParameter(path, "keywords", string.IsNullOrWhiteSpace(query.Keywords) ? query.Series : query.Keywords);
        AppendParameter(path, "title", query.Title);
        AppendParameter(path, "author", query.Author);
        var relative = path.ToString();
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

        return products?.Products.Select(p => ToItem(p, marketCode)).OfType<CatalogItem>().ToList() ?? [];
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
