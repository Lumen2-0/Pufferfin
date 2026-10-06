using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// The detailed sheet of an audiobook, from Audible, with its availability in the local library.
/// </summary>
public sealed record CatalogAudiobookDetails
{
    /// <summary>
    /// Gets the Audible ASIN, upper case.
    /// </summary>
    public required string Asin { get; init; }

    /// <summary>
    /// Gets the title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the subtitle.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Gets the authors, in Audible order.
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>
    /// Gets the narrators, in Audible order.
    /// </summary>
    public IReadOnlyList<string> Narrators { get; init; } = [];

    /// <summary>
    /// Gets the series the book belongs to.
    /// </summary>
    public string? SeriesName { get; init; }

    /// <summary>
    /// Gets the position of the book in its series (<c>1</c>, <c>2.5</c>), as Audible gives it.
    /// </summary>
    public string? SeriesPosition { get; init; }

    /// <summary>
    /// Gets the length of the reading, in minutes.
    /// </summary>
    public int? RuntimeMinutes { get; init; }

    /// <summary>
    /// Gets the cover URL.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// Gets the summary, without HTML.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the language as Audible names it (<c>french</c>), like the search results.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the ISO 639-1 language (<c>fr</c>), when Audible gives a known one. The language of a request for this book.
    /// </summary>
    public string? LanguageCode { get; init; }

    /// <summary>
    /// Gets the Audible market the sheet was read from.
    /// </summary>
    public required string Market { get; init; }

    /// <summary>
    /// Gets the release date.
    /// </summary>
    public DateOnly? ReleaseDate { get; init; }

    /// <summary>
    /// Gets the release year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets a value indicating whether the book is in the local library.
    /// </summary>
    public required CatalogAvailability Availability { get; init; }

    /// <summary>
    /// Gets the id of the local item, when the book is in the library.
    /// </summary>
    public Guid? LocalItemId { get; init; }
}
