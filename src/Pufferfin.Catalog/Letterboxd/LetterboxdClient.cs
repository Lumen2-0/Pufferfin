using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Pufferfin.Catalog.Caching;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Letterboxd;

/// <summary>
/// Reads the public diary feed of a Letterboxd member (<c>https://letterboxd.com/{user}/rss/</c>).
/// </summary>
/// <remarks>
/// Letterboxd has no public API; the feed carries the TMDB id of every film, which is all the recommendations need.
/// It only lists the most recent entries.
/// </remarks>
public sealed class LetterboxdClient
{
    /// <summary>
    /// The name of the HTTP client registered for Letterboxd.
    /// </summary>
    public const string HttpClientName = "Pufferfin.Letterboxd";

    private static readonly Uri _baseAddress = new("https://letterboxd.com/");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICatalogCache _cache;
    private readonly CatalogRateLimiters _rateLimiters;

    /// <summary>
    /// Initializes a new instance of the <see cref="LetterboxdClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="cache">The catalog cache.</param>
    /// <param name="rateLimiters">The outbound rate limiters.</param>
    public LetterboxdClient(IHttpClientFactory httpClientFactory, ICatalogCache cache, CatalogRateLimiters rateLimiters)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _rateLimiters = rateLimiters;
    }

    /// <summary>
    /// Gets the films of the diary of a member.
    /// </summary>
    /// <param name="username">The Letterboxd username.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The diary entries, most recent first.</returns>
    public async Task<IReadOnlyList<LetterboxdDiaryEntry>> GetDiaryAsync(string username, CancellationToken cancellationToken)
    {
        var path = Uri.EscapeDataString(username.Trim().ToLowerInvariant()) + "/rss/";
        var entries = await _cache.GetOrCreateAsync<List<LetterboxdDiaryEntry>>(
            "letterboxd:" + path,
            CatalogCacheDurations.Letterboxd,
            async ct =>
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                var body = await CatalogHttp.GetStringAsync(
                    client,
                    () => new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, path)),
                    CatalogSource.Letterboxd,
                    _rateLimiters.Letterboxd,
                    ct).ConfigureAwait(false);

                // An unknown member answers 404: nothing to recommend from, reported as unavailable.
                return body is null
                    ? throw new CatalogSourceException(CatalogSource.Letterboxd, CatalogSourceStatus.Unavailable)
                    : ParseFeed(body);
            },
            cancellationToken).ConfigureAwait(false);
        return entries ?? [];
    }

    /// <summary>
    /// Parses a diary feed.
    /// </summary>
    /// <param name="xml">The RSS document.</param>
    /// <returns>The entries that carry a TMDB film id.</returns>
    internal static List<LetterboxdDiaryEntry> ParseFeed(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new CatalogSourceException(CatalogSource.Letterboxd, CatalogSourceStatus.Unavailable, ex);
        }

        // Matched by local name: the feed namespaces have changed scheme (http/https) over time.
        static string? Child(XElement item, string localName)
            => item.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

        var result = new List<LetterboxdDiaryEntry>();
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            if (!int.TryParse(Child(item, "movieId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
            {
                continue;
            }

            result.Add(new LetterboxdDiaryEntry
            {
                TmdbId = tmdbId,
                Title = Child(item, "filmTitle"),
                Rating = double.TryParse(Child(item, "memberRating"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) ? rating : null,
                WatchedDate = DateOnly.TryParseExact(Child(item, "watchedDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null
            });
        }

        return result;
    }
}
