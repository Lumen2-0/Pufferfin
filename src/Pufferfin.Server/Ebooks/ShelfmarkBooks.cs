using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Reads the books of the Shelfmark metadata API (<c>/api/metadata/search</c>, <c>/api/metadata/book</c>).
/// </summary>
public static class ShelfmarkBooks
{
    /// <summary>
    /// The prefix of the catalog key of a book: <c>shelfmark:{provider}:{id}</c>.
    /// </summary>
    public const string KeyPrefix = "shelfmark:";

    /// <summary>
    /// Reads a book.
    /// </summary>
    /// <param name="node">The book, as Shelfmark serializes its <c>BookMetadata</c>.</param>
    /// <param name="provider">The provider, when the book does not name it.</param>
    /// <returns>The book, or <c>null</c> without a provider, an id or a title.</returns>
    public static EbookMetadata? From(JsonNode? node, string? provider)
    {
        if (node is not JsonObject book
            || (Text(book, "provider") ?? provider) is not { } providerName
            || Text(book, "provider_id") is not { } id
            || Text(book, "title") is not { } title)
        {
            return null;
        }

        var authors = book["authors"] is JsonArray list
            ? list.OfType<JsonValue>().Select(a => a.TryGetValue<string>(out var name) ? name.Trim() : null).OfType<string>().Where(a => a.Length > 0).ToList()
            : [];
        var subtitle = Text(book, "subtitle");
        return new EbookMetadata(
            providerName.ToLowerInvariant(),
            id,
            title,
            authors,
            Text(book, "series_name"),
            Position(book["series_position"]),
            LanguageCode(Text(book, "language")),
            CoverUrl(Text(book, "cover_url")),
            book["publish_year"] is JsonValue year && year.TryGetValue<int>(out var published) && published > 0 ? published : null,
            Text(book, "description"),
            AudiobookTitles.IsCompleteEdition(title) || (subtitle is not null && AudiobookTitles.IsCompleteEdition(subtitle)));
    }

    /// <summary>
    /// Converts a book into a catalog entry, missing from the library until the merge marks it.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns>The entry.</returns>
    public static CatalogItem ToCatalogItem(EbookMetadata book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return new CatalogItem
        {
            Key = KeyPrefix + book.Provider + ":" + book.BookId,
            Kind = CatalogItemKind.Book,
            Title = book.Title,
            Year = book.Year,
            Overview = book.Overview,
            ImageUrl = book.CoverUrl,
            Availability = CatalogAvailability.Missing,
            BookProvider = book.Provider,
            BookId = book.BookId,
            Authors = book.Authors,
            SeriesName = book.SeriesName is null || book.SeriesPosition is null ? book.SeriesName : book.SeriesName + ", " + book.SeriesPosition,
            Language = book.Language,
            IsCompleteEdition = book.IsCompleteEdition,
            Sources = [CatalogSource.Shelfmark]
        };
    }

    /// <summary>
    /// Reads the cover of a book as an absolute HTTPS URL the client can load. With its cover cache on, Shelfmark
    /// replaces the cover by a path of its own (<c>/api/covers/{id}?url={original, base64url}</c>) that needs its key:
    /// the original is read back from it.
    /// </summary>
    /// <param name="coverUrl">The cover Shelfmark gives.</param>
    /// <returns>The URL, or <c>null</c> when there is no usable one.</returns>
    public static string? CoverUrl(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            return null;
        }

        if (!coverUrl.StartsWith('/'))
        {
            return Https(coverUrl);
        }

        var query = coverUrl.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
        {
            return null;
        }

        foreach (var parameter in coverUrl[(query + 1)..].Split('&'))
        {
            if (!parameter.StartsWith("url=", StringComparison.Ordinal))
            {
                continue;
            }

            var encoded = Uri.UnescapeDataString(parameter[4..]).Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '=');
            try
            {
                return Https(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a language as an ISO 639-1 code: <c>fr</c>, <c>fre</c>, <c>fra</c>, <c>fr-FR</c> or <c>French</c>.
    /// </summary>
    /// <param name="language">The language a provider gives.</param>
    /// <returns>The code, or <c>null</c> when it is not a known language.</returns>
    public static string? LanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var value = language.Trim();
        var primary = value.Split('-', '_')[0];
        if (primary.Length == 2 && primary.All(char.IsAsciiLetter))
        {
            return primary.ToLowerInvariant();
        }

        // ISO 639-2 has bibliographic codes (fre, ger) next to the terminology codes .NET knows (fra, deu).
        var bibliographic = primary.ToLowerInvariant() switch
        {
            "fre" => "fr",
            "ger" => "de",
            "dut" => "nl",
            "chi" => "zh",
            "cze" => "cs",
            "gre" => "el",
            _ => null
        };
        if (bibliographic is not null)
        {
            return bibliographic;
        }

        var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(c => c.TwoLetterISOLanguageName.Length == 2
            && (string.Equals(c.ThreeLetterISOLanguageName, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.EnglishName, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.NativeName, value, StringComparison.OrdinalIgnoreCase)));
        return culture?.TwoLetterISOLanguageName;
    }

    private static string? Https(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    private static string? Position(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<double>(out var position) && position > 0
            ? position.ToString("0.##", CultureInfo.InvariantCulture)
            : null;

    private static string? Text(JsonObject node, string name)
        => node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
