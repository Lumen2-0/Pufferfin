using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Ebooks;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// The Shelfmark API (version 1.4 and later): book metadata, release search, download queue and status, for audiobooks and ebooks.
/// The key travels in the <c>X-Api-Key</c> header; every call has an explicit time limit (see <see cref="ArrHttpClient"/>).
/// </summary>
public sealed class ShelfmarkClient
{
    /// <summary>
    /// The content type of an audiobook for Shelfmark.
    /// </summary>
    public const string AudiobookContentType = "audiobook";

    /// <summary>
    /// The content type of an ebook for Shelfmark: books to read, comic book archives included.
    /// </summary>
    public const string EbookContentType = "ebook";

    private readonly ArrHttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShelfmarkClient"/> class.
    /// </summary>
    /// <param name="http">The HTTP client of the services.</param>
    public ShelfmarkClient(ArrHttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Searches the audiobook releases of a book in every enabled source of Shelfmark. Slow: it waits on the indexers.
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="book">The book.</param>
    /// <param name="language">The ISO 639-1 language to search for, or <c>null</c> for the default languages of Shelfmark.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The releases.</returns>
    /// <exception cref="ArrApiException">The call failed. Shelfmark answers 503 when no source could be searched.</exception>
    public async Task<IReadOnlyList<ShelfmarkRelease>> SearchAudiobookReleasesAsync(ServiceSettings service, AudiobookMetadata book, string? language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        // The "manual" provider searches the sources with the title and the author given here, without a metadata provider.
        var query = new StringBuilder("api/releases?provider=manual&content_type=")
            .Append(AudiobookContentType)
            .Append("&book_id=").Append(Uri.EscapeDataString(book.Asin))
            .Append("&title=").Append(Uri.EscapeDataString(book.Title))
            .Append("&author=").Append(Uri.EscapeDataString(string.Join(", ", book.Authors)));
        if (!string.IsNullOrWhiteSpace(language))
        {
            query.Append("&languages=").Append(Uri.EscapeDataString(language));
        }

        var answer = await _http.GetAsync<JsonObject>(service, query.ToString(), cancellationToken, isSearch: true).ConfigureAwait(false);
        return Releases(answer, AudiobookContentType);
    }

    /// <summary>
    /// Searches books in the metadata provider Shelfmark is configured with for ebooks (Hardcover, Open Library or Google Books).
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="query">The search text.</param>
    /// <param name="limit">The size of a page.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The books of the page, each with the provider it comes from.</returns>
    /// <exception cref="ArrApiException">The call failed. Shelfmark answers 503 when no metadata provider is enabled or available.</exception>
    public async Task<IReadOnlyList<EbookMetadata>> SearchBooksAsync(ServiceSettings service, string query, int limit, int page, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"api/metadata/search?content_type={EbookContentType}&query={Uri.EscapeDataString(query)}&limit={limit}&page={page}");
        var answer = await _http.GetAsync<JsonObject>(service, path, cancellationToken).ConfigureAwait(false);

        // Each book names its provider; the answer names the one used, for books that would not.
        var provider = answer["provider"] is JsonValue value && value.TryGetValue<string>(out var name) ? name : null;
        return answer["books"] is JsonArray books ? books.Select(b => ShelfmarkBooks.From(b, provider)).OfType<EbookMetadata>().ToList() : [];
    }

    /// <summary>
    /// Gets a book from a metadata provider of Shelfmark.
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="provider">The provider (<c>hardcover</c>, <c>openlibrary</c>, <c>googlebooks</c>).</param>
    /// <param name="bookId">The id of the book in that provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The book, or <c>null</c> when the provider does not know it.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public async Task<EbookMetadata?> GetBookAsync(ServiceSettings service, string provider, string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        try
        {
            var answer = await _http.GetAsync<JsonObject>(
                service,
                "api/metadata/book/" + Uri.EscapeDataString(provider) + "/" + Uri.EscapeDataString(bookId),
                cancellationToken).ConfigureAwait(false);
            return ShelfmarkBooks.From(answer, provider);
        }
        catch (ArrApiException ex) when (ex.Failure == ArrFailure.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Searches the ebook releases of a book in every enabled source of Shelfmark. Slow: it waits on the indexers.
    /// Only ebooks: an audiobook release Shelfmark would still return is dropped.
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="book">The book, from a metadata provider of Shelfmark.</param>
    /// <param name="language">The ISO 639-1 language to search for, or <c>null</c> for the default languages of Shelfmark.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The releases.</returns>
    /// <exception cref="ArrApiException">The call failed. Shelfmark answers 503 when no source could be searched.</exception>
    public async Task<IReadOnlyList<ShelfmarkRelease>> SearchEbookReleasesAsync(ServiceSettings service, EbookMetadata book, string? language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        // The provider and its id: Shelfmark reads the book there, with its ISBN and its localized titles.
        var query = new StringBuilder("api/releases?content_type=")
            .Append(EbookContentType)
            .Append("&provider=").Append(Uri.EscapeDataString(book.Provider))
            .Append("&book_id=").Append(Uri.EscapeDataString(book.BookId))
            .Append("&title=").Append(Uri.EscapeDataString(book.Title));
        if (!string.IsNullOrWhiteSpace(language))
        {
            query.Append("&languages=").Append(Uri.EscapeDataString(language));
        }

        var answer = await _http.GetAsync<JsonObject>(service, query.ToString(), cancellationToken, isSearch: true).ConfigureAwait(false);
        return Releases(answer, EbookContentType);
    }

    /// <summary>
    /// Queues a release for download. Never retried: a release queued twice is a double download.
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="release">The release, as Shelfmark returned it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public Task QueueAsync(ServiceSettings service, ShelfmarkRelease release, CancellationToken cancellationToken)
        => QueueAsync(service, release, AudiobookContentType, cancellationToken);

    /// <summary>
    /// Queues a release for download as a given content type, which decides its qBittorrent category and its folder in
    /// Shelfmark. Never retried: a release queued twice is a double download.
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="release">The release, as Shelfmark returned it.</param>
    /// <param name="contentType"><see cref="AudiobookContentType"/> or <see cref="EbookContentType"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public Task QueueAsync(ServiceSettings service, ShelfmarkRelease release, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrEmpty(contentType);
        var body = release.Raw.DeepClone().AsObject();
        body["content_type"] = contentType;
        return _http.SendAsync(service, HttpMethod.Post, "api/releases/download", body, cancellationToken);
    }

    /// <summary>
    /// Gets the downloads Shelfmark knows: queued, in progress, complete, failed and cancelled.
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The downloads, by id.</returns>
    /// <exception cref="ArrApiException">The call failed, or the key was refused.</exception>
    public async Task<IReadOnlyDictionary<string, ShelfmarkDownload>> GetDownloadsAsync(ServiceSettings service, CancellationToken cancellationToken)
    {
        // { "<status>": { "<id>": { ... } } }. The status of the bucket wins over the one inside the entry.
        var answer = await _http.GetAsync<JsonObject>(service, "api/status", cancellationToken).ConfigureAwait(false);
        var downloads = new Dictionary<string, ShelfmarkDownload>(StringComparer.Ordinal);
        foreach (var (status, bucket) in answer)
        {
            if (bucket is not JsonObject entries)
            {
                continue;
            }

            foreach (var (id, entry) in entries)
            {
                ShelfmarkDownload? download;
                try
                {
                    download = entry?.Deserialize<ShelfmarkDownload>(ArrHttpClient.JsonOptions);
                }
                catch (JsonException)
                {
                    throw new ArrApiException(PufferfinService.Shelfmark, ArrFailure.InvalidResponse);
                }

                if (download is not null)
                {
                    download.Id ??= id;
                    download.Status = status;
                    downloads[download.Id] = download;
                }
            }
        }

        return downloads;
    }

    // The releases of one content type: Shelfmark filters on it, and a release that says it is of another type is dropped all the same.
    private static List<ShelfmarkRelease> Releases(JsonObject answer, string contentType)
        => answer["releases"] is JsonArray releases
            ? releases.Select(ShelfmarkRelease.From)
                .OfType<ShelfmarkRelease>()
                .Where(r => r.ContentType is null || string.Equals(r.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];
}
