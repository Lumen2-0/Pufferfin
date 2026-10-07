using System.Collections.Generic;
using Pufferfin.Server.Audiobooks;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// What the Requests module needs to know about a book to read, from a metadata provider of Shelfmark.
/// </summary>
/// <param name="Provider">The metadata provider (<c>hardcover</c>, <c>openlibrary</c>, <c>googlebooks</c>).</param>
/// <param name="BookId">The id of the book in that provider. The providers do not share their ids.</param>
/// <param name="Title">The title.</param>
/// <param name="Authors">The authors.</param>
/// <param name="SeriesName">The series, when the book belongs to one.</param>
/// <param name="SeriesPosition">The position of the book in its series (<c>1</c>, <c>2.5</c>), when known.</param>
/// <param name="Language">The ISO 639-1 language of the book, when the provider gives a known one.</param>
/// <param name="CoverUrl">The cover, an absolute HTTPS URL, when the provider has one.</param>
/// <param name="Year">The year of publication, when known.</param>
/// <param name="Overview">The description, when the provider has one.</param>
/// <param name="IsCompleteEdition">Whether the book is itself a complete edition, an omnibus or a box set: only such a release fulfils it.</param>
public sealed record EbookMetadata(
    string Provider,
    string BookId,
    string Title,
    IReadOnlyList<string> Authors,
    string? SeriesName,
    string? SeriesPosition,
    string? Language,
    string? CoverUrl,
    int? Year = null,
    string? Overview = null,
    bool IsCompleteEdition = false)
{
    /// <summary>
    /// Gets the book as the release ranking of the audiobooks reads it: one rule for the title, the volume, the language
    /// and the complete editions, whether the book is read or listened to.
    /// </summary>
    /// <returns>The book, for <see cref="AudiobookReleaseSelector"/>.</returns>
    internal AudiobookMetadata AsMatchTarget()
        => new(BookId, Title, Authors, SeriesName, SeriesPosition, Language, CoverUrl, Market: null, Narrators: null, IsCompleteEdition);
}
