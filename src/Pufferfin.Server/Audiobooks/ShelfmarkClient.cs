using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// The Shelfmark API (version 1.4 and later): release search, download queue and status, for audiobooks.
/// The key travels in the <c>X-Api-Key</c> header; every call has an explicit time limit (see <see cref="ArrHttpClient"/>).
/// </summary>
public sealed class ShelfmarkClient
{
    /// <summary>
    /// The content type of an audiobook for Shelfmark.
    /// </summary>
    public const string AudiobookContentType = "audiobook";

    /// <summary>
    /// The content type of an ebook for Shelfmark. Pufferfin offers no books to read, but some trackers file audiobooks
    /// with the books: the audiobook search also looks there, and a download of this type is never taken for an audiobook.
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
    /// Searches the audiobooks some trackers file with the books to read (YggReborn: Books, Audio books): the same
    /// search in the book categories. The caller keeps the audio releases only.
    /// </summary>
    /// <param name="service">The Shelfmark settings.</param>
    /// <param name="book">The audiobook.</param>
    /// <param name="language">The wanted ISO 639-1 language, or <c>null</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The releases, audio or not.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public async Task<IReadOnlyList<ShelfmarkRelease>> SearchAudiobookReleasesInBooksAsync(ServiceSettings service, AudiobookMetadata book, string? language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        var query = new StringBuilder("api/releases?provider=manual&content_type=")
            .Append(EbookContentType)
            .Append("&book_id=").Append(Uri.EscapeDataString(book.Asin))
            .Append("&title=").Append(Uri.EscapeDataString(book.Title))
            .Append("&author=").Append(Uri.EscapeDataString(string.Join(", ", book.Authors)));
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
    {
        ArgumentNullException.ThrowIfNull(release);

        // As an audiobook, wherever it was found: the content type decides its qBittorrent category and its folder in Shelfmark.
        var body = release.Raw.DeepClone().AsObject();
        body["content_type"] = AudiobookContentType;
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
