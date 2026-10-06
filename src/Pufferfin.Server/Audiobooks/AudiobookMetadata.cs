using System.Collections.Generic;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// What the Requests module needs to know about an audiobook of the Audible catalog.
/// </summary>
/// <param name="Asin">The ASIN, upper case.</param>
/// <param name="Title">The title.</param>
/// <param name="Authors">The authors, in Audible order.</param>
/// <param name="SeriesName">The series, when the book belongs to one.</param>
/// <param name="SeriesPosition">The position of the book in its series (<c>1</c>, <c>2.5</c>), when known.</param>
/// <param name="Language">The ISO 639-1 language of the book, when Audible gives a known one.</param>
/// <param name="CoverUrl">The cover, when Audible has one.</param>
/// <param name="Market">The Audible market the book was read from.</param>
/// <param name="Narrators">The narrators, whose names release titles often carry.</param>
public sealed record AudiobookMetadata(
    string Asin,
    string Title,
    IReadOnlyList<string> Authors,
    string? SeriesName,
    string? SeriesPosition,
    string? Language,
    string? CoverUrl,
    string? Market,
    IReadOnlyList<string>? Narrators = null);
