using System;
using System.Text.Json.Nodes;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// A release Shelfmark found for a book. The raw object is what Shelfmark expects back to queue it: it may carry a
/// download URL with a tracker passkey, so it never leaves the server and is never logged nor stored.
/// </summary>
public sealed class ShelfmarkRelease
{
    private ShelfmarkRelease(JsonObject raw, string source, string sourceId, string title)
    {
        Raw = raw;
        Source = source;
        SourceId = sourceId;
        Title = title;
    }

    /// <summary>Gets the release source (<c>prowlarr</c>, <c>audiobookbay</c>...).</summary>
    public string Source { get; }

    /// <summary>Gets the id of the release in its source; also the id of the Shelfmark download.</summary>
    public string SourceId { get; }

    /// <summary>Gets the release title.</summary>
    public string Title { get; }

    /// <summary>Gets the ISO 639-1 language Shelfmark reports, when it reports one.</summary>
    public string? Language => Text("language");

    /// <summary>Gets the number of seeders, for a torrent.</summary>
    public int? Seeders => Raw["seeders"] is JsonValue value && value.TryGetValue<int>(out var seeders) ? seeders : null;

    /// <summary>Gets the content type Shelfmark reports (<c>audiobook</c>, <c>ebook</c>).</summary>
    public string? ContentType => Text("content_type");

    /// <summary>Gets the file format Shelfmark reports (<c>epub</c>, <c>pdf</c>, <c>zip</c>...), lower case.</summary>
    public string? Format => Text("format")?.TrimStart('.').ToLowerInvariant();

    /// <summary>Gets how the release downloads: <c>torrent</c>, <c>nzb</c>, <c>http</c> (a direct download) or <c>dcc</c> (IRC).</summary>
    public string? Protocol => Text("protocol")?.ToLowerInvariant();

    /// <summary>Gets the raw release, as Shelfmark returned it. Secret: may carry a passkey.</summary>
    internal JsonObject Raw { get; }

    /// <summary>
    /// Reads a release from the answer of Shelfmark.
    /// </summary>
    /// <param name="node">One entry of <c>releases</c>.</param>
    /// <returns>The release, or <c>null</c> when it has no source, id or title.</returns>
    public static ShelfmarkRelease? From(JsonNode? node)
    {
        if (node is not JsonObject raw
            || raw["source"] is not JsonValue source || !source.TryGetValue<string>(out var sourceName) || string.IsNullOrWhiteSpace(sourceName)
            || raw["source_id"] is not JsonValue id || !id.TryGetValue<string>(out var sourceId) || string.IsNullOrWhiteSpace(sourceId)
            || raw["title"] is not JsonValue title || !title.TryGetValue<string>(out var titleText) || string.IsNullOrWhiteSpace(titleText))
        {
            return null;
        }

        return new ShelfmarkRelease(raw, sourceName, sourceId, titleText);
    }

    /// <summary>
    /// Gets a value indicating whether the release is in a language.
    /// </summary>
    /// <param name="language">An ISO 639-1 code.</param>
    /// <returns><c>true</c> when Shelfmark reports that language.</returns>
    public bool IsIn(string language)
        => Language is { } own && string.Equals(own.Split('-', 2)[0], language.Split('-', 2)[0], StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string ToString() => $"{nameof(ShelfmarkRelease)} {{ Source = {Source}, Title = {Title} }}";

    /// <summary>
    /// Creates a release that only has a title, to compare a title with a book.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The release.</returns>
    internal static ShelfmarkRelease Untitled(string title) => new(new JsonObject(), string.Empty, string.Empty, title);

    private string? Text(string name)
        => Raw[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
