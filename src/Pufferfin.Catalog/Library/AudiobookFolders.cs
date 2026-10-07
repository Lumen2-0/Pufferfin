using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// Groups the files of a book told in several files into that one book.
/// </summary>
/// <remarks>
/// <para>
/// The server makes one <see cref="AudioBook"/> per file when a book folder holds several (upstream does not stack
/// multi-part books yet), so a book in 13 chapters shows as 13 books. Pufferfin shows the folder instead: a folder
/// whose audio files all share one name, numbers aside (<c>Title - Part 01</c>, <c>Title - Part 02</c>), is one book.
/// </para>
/// <para>
/// A folder holding several different books (loose files of an author) is left as it is: each file stays a book.
/// </para>
/// </remarks>
public static partial class AudiobookFolders
{
    /// <summary>
    /// Gets the name of a file of a book without what tells the parts apart: the extension, the part, chapter, disc or
    /// track numbers, punctuation and case.
    /// </summary>
    /// <param name="path">The path or the name of the file.</param>
    /// <returns>The stem, the same for every part of one book.</returns>
    public static string GetStem(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        name = PartWordRegex().Replace(name, " ");
        name = DigitsRegex().Replace(name, " ");
        name = SeparatorRegex().Replace(name, " ");
        return name.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Gets a value indicating whether files are the parts of one book: at least two, all with the same stem, and a
    /// stem that names something (files named by their numbers only, such as <c>Book 1</c>, <c>Book 2</c>, may be
    /// different books).
    /// </summary>
    /// <param name="paths">The paths of the audio files of a folder.</param>
    /// <returns><c>true</c> for the parts of one book.</returns>
    public static bool AreOneBook(IReadOnlyCollection<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count < 2)
        {
            return false;
        }

        var stems = paths.Select(GetStem).Distinct(StringComparer.Ordinal).ToList();
        return stems.Count == 1 && stems[0].Length > 0;
    }

    /// <summary>
    /// Gets the parts of a book folder, in reading order, or nothing when the folder is not one book in several files.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <returns>The parts, sorted by path.</returns>
    public static IReadOnlyList<AudioBook> GetParts(BaseItem folder, ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(libraryManager);

        // A plain folder of a library only: never a library itself, nor the root of the libraries
        if (!folder.IsFolder || folder.IsTopParent || folder is CollectionFolder or UserRootFolder or AggregateFolder)
        {
            return [];
        }

        var parts = libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = folder.Id,
            IncludeItemTypes = [BaseItemKind.AudioBook],
            Recursive = false
        })
            .OfType<AudioBook>()
            .Where(p => !string.IsNullOrEmpty(p.Path))
            .OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return AreOneBook(parts.Select(p => p.Path).ToList()) ? parts : [];
    }

    /// <summary>
    /// Gets the book a file belongs to: its folder when the folder is one book in several files, else the file itself.
    /// </summary>
    /// <param name="audioBook">The file.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <returns>The book.</returns>
    public static BaseItem GetBook(AudioBook audioBook, ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(audioBook);
        ArgumentNullException.ThrowIfNull(libraryManager);
        return audioBook.GetParent() is { } parent && GetParts(parent, libraryManager).Count > 0 ? parent : audioBook;
    }

    /// <summary>
    /// Gets the authors of a book: those of its first file, else the name of the folder above the book folder, which
    /// is the author in the usual <c>Author/Title</c> layout.
    /// </summary>
    /// <param name="book">The book: a file, or a folder of parts.</param>
    /// <param name="parts">The parts, for a folder.</param>
    /// <returns>The authors.</returns>
    public static IReadOnlyList<string> GetAuthors(BaseItem book, IReadOnlyList<AudioBook> parts)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(parts);
        var first = book as AudioBook ?? parts.FirstOrDefault();
        var authors = first is null
            ? []
            : first.AlbumArtists.Concat(first.Artists).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (authors.Count > 0 || book is AudioBook)
        {
            return authors;
        }

        var authorFolder = Path.GetFileName(Path.GetDirectoryName(book.Path));
        return string.IsNullOrWhiteSpace(authorFolder) ? [] : [authorFolder];
    }

    // "Part 01", "pt.2", "Chapitre 3", "CD1", "Disc 2", "Track 04", "Partie 5"
    // The number may follow the word directly ("CD2") or after a dot or spaces ("pt. 2", "Part 01")
    [GeneratedRegex(@"\b(?:part|pt|partie|chapter|chapitre|ch|cd|disc|disk|track|piste|tome|book|livre)\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartWordRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"[\s\p{P}\p{S}]+")]
    private static partial Regex SeparatorRegex();
}
