using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Reads the weekly trending books of Open Library (<c>/trending/weekly.json</c>, public, no key).
/// </summary>
/// <remarks>
/// Open Library is the default metadata provider of Shelfmark, and its works ids (<c>OL…W</c>) are the ids Shelfmark
/// gives its books: a trending book opens on the same sheet, and downloads the same way, as one found by a search.
/// </remarks>
public static class OpenLibraryTrending
{
    /// <summary>
    /// The name of the Shelfmark metadata provider whose ids the trending books carry.
    /// </summary>
    public const string Provider = "openlibrary";

    private const string WorksPrefix = "/works/";

    private static readonly Uri _baseAddress = new("https://openlibrary.org/");

    /// <summary>
    /// Fetches the trending books.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="limit">The maximum number of books.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The books, most trending first.</returns>
    public static async Task<IReadOnlyList<EbookMetadata>> FetchAsync(HttpClient client, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var uri = new Uri(_baseAddress, "trending/weekly.json?limit=" + Math.Clamp(limit, 1, 100).ToString(CultureInfo.InvariantCulture));
        using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(body);
    }

    /// <summary>
    /// Reads the books of an answer of <c>/trending/weekly.json</c>: those without an id, a title or a cover are left out.
    /// </summary>
    /// <param name="json">The answer.</param>
    /// <returns>The books.</returns>
    public static IReadOnlyList<EbookMetadata> Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        if (root?["works"] is not JsonArray works)
        {
            return [];
        }

        var books = new List<EbookMetadata>();
        foreach (var work in works.OfType<JsonObject>())
        {
            // Works only: "/works/OL82563W" -> "OL82563W" (an author or an edition has another id)
            var key = Text(work, "key");
            var id = key is not null && key.StartsWith(WorksPrefix, StringComparison.Ordinal) ? key[WorksPrefix.Length..] : null;
            var title = Text(work, "title");
            var cover = work["cover_i"] is JsonValue c && c.TryGetValue<long>(out var coverId) && coverId > 0 ? coverId : (long?)null;
            if (id is null || !id.StartsWith("OL", StringComparison.Ordinal) || !id.EndsWith('W') || string.IsNullOrWhiteSpace(title) || cover is null)
            {
                continue;
            }

            var authors = work["author_name"] is JsonArray names
                ? names.Select(n => n?.GetValue<string>()).OfType<string>().Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                : [];
            var year = work["first_publish_year"] is JsonValue y && y.TryGetValue<int>(out var firstYear) ? firstYear : (int?)null;
            books.Add(new EbookMetadata(
                Provider,
                id,
                title.Trim(),
                authors,
                SeriesName: null,
                SeriesPosition: null,
                Language: null,
                CoverUrl: "https://covers.openlibrary.org/b/id/" + cover.Value.ToString(CultureInfo.InvariantCulture) + "-L.jpg",
                Year: year));
        }

        return books;
    }

    private static string? Text(JsonObject node, string name)
        => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
