using System.Collections.Generic;
using MediaBrowser.Model.Dto;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// One book of the library with its parts in reading order, for the page of the book.
/// </summary>
public sealed class PufferfinBookDetails
{
    /// <summary>
    /// Gets the book: the folder of a book in parts, with its authors, length and progress, or the file of a book in one file.
    /// </summary>
    public required BaseItemDto Book { get; init; }

    /// <summary>
    /// Gets the parts, in reading order: one for a book in one file, none for a book to read.
    /// </summary>
    public required IReadOnlyList<BaseItemDto> Parts { get; init; }
}
