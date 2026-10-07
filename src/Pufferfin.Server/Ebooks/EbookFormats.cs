using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pufferfin.Server.Audiobooks;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// The book formats Pufferfin files, the archives it opens, and the formats it leaves aside.
/// </summary>
/// <remarks>
/// <para>
/// Filed: the formats the web client reads and Jellyfin indexes in a <c>books</c> library. EPUB first (the book player),
/// then PDF (the PDF player), then the comic book archives (the comics player): <c>cbz</c>, <c>cbr</c>, <c>cb7</c>, <c>cbt</c>.
/// </para>
/// <para>
/// Opened: a ZIP archive, whose best book file Pufferfin extracts itself. A RAR archive is not opened by Pufferfin; with
/// its default settings Shelfmark extracts ZIP and RAR archives before it reports the download complete, so a RAR only
/// reaches Pufferfin when Shelfmark hard links its book torrents, and the import then stops with that reason.
/// </para>
/// <para>
/// Left aside: MOBI, AZW, AZW3, FB2, DJVU and the text formats. Jellyfin indexes MOBI and AZW3, but no player of the
/// client opens them: a download that cannot be read is not delivered.
/// </para>
/// </remarks>
public static class EbookFormats
{
    // After the filed formats and the archives: a release that names no format at all.
    private const int UnknownRank = 20;

    private static readonly string[] _filed = ["epub", "pdf", "cbz", "cbr", "cb7", "cbt"];

    private static readonly string[] _archives = ["zip", "rar"];

    private static readonly FrozenSet<string> _leftAside = new[]
    {
        "mobi", "azw", "azw3", "kfx", "fb2", "djvu", "lit", "doc", "docx", "rtf", "txt", "html", "htm",
        "m4b", "m4a", "mp3", "aac", "flac", "ogg", "opus", "wma", "wav", "aax", "aaxc"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Gets the formats filed in the library, best first.
    /// </summary>
    public static IReadOnlyList<string> Filed => _filed;

    /// <summary>
    /// Gets the archives a release may come in, best first.
    /// </summary>
    public static IReadOnlyList<string> Archives => _archives;

    /// <summary>
    /// Gets a value indicating whether a file is a book Pufferfin files.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><c>true</c> for a filed format.</returns>
    public static bool IsBook(string path) => Filed.Contains(Extension(path));

    /// <summary>
    /// Gets a value indicating whether a file is a ZIP archive Pufferfin opens.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><c>true</c> for a ZIP archive.</returns>
    public static bool IsZip(string path) => Extension(path) == "zip";

    /// <summary>
    /// Gets a value indicating whether a file is a RAR archive, which Pufferfin does not open.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><c>true</c> for a RAR archive.</returns>
    public static bool IsRar(string path) => Extension(path) == "rar";

    /// <summary>
    /// Gets the rank of a book file: lower is better.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The rank, or <see cref="int.MaxValue"/> for a file that is not filed.</returns>
    public static int RankOf(string path)
    {
        var index = Array.IndexOf(_filed, Extension(path));
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>
    /// Reads the format of a release: the one Shelfmark reports, else the best format its title names.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <returns>The format, lower case, or <c>null</c> when nothing says.</returns>
    public static string? FormatOf(ShelfmarkRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Format is { Length: > 0 } format)
        {
            return format;
        }

        // "Dune (epub, mobi, azw3)": a pack of formats is as good as its best one.
        var words = AudiobookReleaseSelector.Normalize(release.Title).Split(' ').ToHashSet(StringComparer.Ordinal);
        return Filed.Concat(Archives).FirstOrDefault(words.Contains)
            ?? words.FirstOrDefault(_leftAside.Contains);
    }

    /// <summary>
    /// Ranks the format of a release: lower is better.
    /// </summary>
    /// <param name="format">The format, from <see cref="FormatOf"/>.</param>
    /// <returns>The rank, or <c>null</c> for a format Pufferfin leaves aside.</returns>
    public static int? Rank(string? format)
    {
        if (format is null)
        {
            return UnknownRank;
        }

        var filed = Array.IndexOf(_filed, format);
        if (filed >= 0)
        {
            return filed;
        }

        var archive = Array.IndexOf(_archives, format);
        if (archive >= 0)
        {
            return _filed.Length + archive;
        }

        // Left aside, or a format Pufferfin never heard of: not a book the client can open either.
        return null;
    }

    private static string Extension(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
}
