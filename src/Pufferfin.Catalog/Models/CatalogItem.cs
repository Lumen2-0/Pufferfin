using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// One entry of the unified catalog: a title or a person, present on disk or not.
/// </summary>
public sealed record CatalogItem
{
    /// <summary>
    /// Gets the stable key of the entry: <c>tmdb:movie:603</c>, <c>tmdb:series:1399</c>, <c>tmdb:person:6384</c>,
    /// <c>audible:B0036NAKL0</c>, <c>shelfmark:hardcover:312460</c> for a book, or <c>library:{item id}</c> for a local
    /// item with no external match.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the kind of the entry.
    /// </summary>
    public required CatalogItemKind Kind { get; init; }

    /// <summary>
    /// Gets the display title, or the name of a person.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the original title.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// Gets the release year (first air year for a series).
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the overview.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the absolute URL of a remote poster, cover or portrait. Empty for a local only entry: use the item images.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// Gets the absolute URL of a remote backdrop.
    /// </summary>
    public string? BackdropUrl { get; init; }

    /// <summary>
    /// Gets whether the entry is on disk.
    /// </summary>
    public required CatalogAvailability Availability { get; init; }

    /// <summary>
    /// Gets the id of the local library item when the entry is on disk.
    /// </summary>
    public Guid? LocalItemId { get; init; }

    /// <summary>
    /// Gets the TMDB id.
    /// </summary>
    public int? TmdbId { get; init; }

    /// <summary>
    /// Gets the Audible ASIN of an audiobook.
    /// </summary>
    public string? Asin { get; init; }

    /// <summary>
    /// Gets the authors of an audiobook.
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>
    /// Gets the narrators of an audiobook.
    /// </summary>
    public IReadOnlyList<string> Narrators { get; init; } = [];

    /// <summary>
    /// Gets the series an audiobook or a book belongs to, with its position (for example <c>Dune, 1</c>).
    /// </summary>
    public string? SeriesName { get; init; }

    /// <summary>
    /// Gets the language of an audiobook, as reported by Audible, or of a book, as its metadata provider reports it.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the Audible market an audiobook was found in.
    /// </summary>
    public string? Market { get; init; }

    /// <summary>
    /// Gets a value indicating whether an audiobook or a book is a complete edition (several books of a series in one): shown
    /// next to the volumes, downloaded only when asked for itself, never in place of a volume.
    /// </summary>
    public bool IsCompleteEdition { get; init; }

    /// <summary>
    /// Gets the department a person is known for.
    /// </summary>
    public string? KnownForDepartment { get; init; }

    /// <summary>
    /// Gets the role in a credit list: the character for an actor, the job otherwise.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Gets the sources that contributed to this entry.
    /// </summary>
    public IReadOnlyList<CatalogSource> Sources { get; init; } = [];
}
