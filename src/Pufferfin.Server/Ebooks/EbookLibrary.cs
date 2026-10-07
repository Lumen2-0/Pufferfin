using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Files books to read in the library: <c>Author/Title/Title.epub</c> under <see cref="FolderName"/> of the library
/// directory (<c>MEDIA_PATH</c>, <c>/media</c> by default), as a hard link of the downloaded file, so the torrent keeps
/// seeding without taking more space, with the cover of the metadata provider next to it.
/// </summary>
/// <remarks>
/// <para>
/// One book file per folder, the best format of the download (<see cref="EbookFormats"/>): Jellyfin takes a folder for
/// one book only when it holds exactly one book file.
/// </para>
/// <para>
/// A ZIP archive is opened and its best book file extracted: that file is a copy, not a hard link, so it is found by the
/// inode of the archive, not its own. A RAR archive is not opened: the import stops with that reason. Pufferfin sees
/// the disk as Shelfmark and qBittorrent do: the download paths they report must exist at the same path in Pufferfin.
/// </para>
/// </remarks>
public sealed class EbookLibrary
{
    /// <summary>
    /// The folder of the books in the library directory: a Jellyfin library of type <c>books</c> must point to it.
    /// </summary>
    public const string FolderName = "books";

    /// <summary>
    /// The largest book file extracted from an archive. A comic book archive is large; anything larger is not a book.
    /// </summary>
    public const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileLinks _links;
    private readonly Func<string> _root;
    private readonly ILogger<EbookLibrary> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EbookLibrary"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory, for the covers.</param>
    /// <param name="links">The inodes and hard links.</param>
    /// <param name="configuration">The configuration store, for the library directory.</param>
    /// <param name="logger">The logger.</param>
    public EbookLibrary(IHttpClientFactory httpClientFactory, IFileLinks links, IPufferfinConfigurationStore configuration, ILogger<EbookLibrary> logger)
        : this(httpClientFactory, links, () => Path.Join(StoragePaths.From(configuration.Current.Settings).Media, FolderName), logger)
    {
    }

    internal EbookLibrary(IHttpClientFactory httpClientFactory, IFileLinks links, Func<string> root, ILogger<EbookLibrary> logger)
    {
        _httpClientFactory = httpClientFactory;
        _links = links;
        _root = root;
        _logger = logger;
    }

    /// <summary>
    /// Gets the library folder of the books.
    /// </summary>
    public string Root => _root();

    /// <summary>
    /// Gets the folder of a book: <c>Author/Title</c>, each made safe for any file system.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns>The folder.</returns>
    public string FolderOf(EbookMetadata book)
    {
        ArgumentNullException.ThrowIfNull(book);
        var author = book.Authors.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "Unknown author";
        return Path.Combine(Root, AudiobookLibrary.SafeName(author), AudiobookLibrary.SafeName(book.Title));
    }

    /// <summary>
    /// Gets a value indicating whether the library already holds a book: its folder has a book file.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns><c>true</c> when the book is filed.</returns>
    public bool Contains(EbookMetadata book)
    {
        var folder = FolderOf(book);
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any(EbookFormats.IsBook);
    }

    /// <summary>
    /// Files a downloaded book: a hard link of its best book file in <c>Author/Title</c>, or that file extracted from a
    /// ZIP archive, then the cover. Running it again is harmless.
    /// </summary>
    /// <param name="source">The downloaded file or folder, as Shelfmark reports it.</param>
    /// <param name="book">The book.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was filed.</returns>
    /// <exception cref="EbookImportException">The book cannot be filed, with the reason.</exception>
    public async Task<EbookImport> ImportAsync(string source, EbookMetadata book, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(book);
        var found = Find(source);
        var folder = FolderOf(book);
        var extension = found.Archive is null ? Path.GetExtension(found.Path) : Path.GetExtension(found.EntryName!);
        var target = Path.Combine(folder, AudiobookLibrary.SafeName(book.Title) + extension.ToLowerInvariant());
        try
        {
            Directory.CreateDirectory(folder);
            if (found.Archive is null)
            {
                Link(found.Path, target, folder);
            }
            else
            {
                await ExtractAsync(found.Path, found.EntryName!, target, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new EbookImportException($"The library folder {folder} cannot be written: {ex.Message}");
        }

        var cover = await AudiobookLibrary.WriteCoverAsync(_httpClientFactory, _logger, folder, book.CoverUrl, cancellationToken).ConfigureAwait(false);

        // The files a torrent shares with the library: the book file itself, or the archive it was extracted from.
        return new EbookImport(folder, target, found.Archive is null ? target : found.Path, found.Archive is not null, cover, AudiobookLibrary.FindNotWritable(folder));
    }

    // The best book file of a download, else the best book file of its best ZIP archive.
    private static Found Find(string source)
    {
        List<string> files;
        if (File.Exists(source))
        {
            files = [source];
        }
        else if (Directory.Exists(source))
        {
            files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
        }
        else
        {
            throw new EbookImportException($"The download is not visible from Pufferfin at {source}. Mount the downloads at the same path in Pufferfin as in Shelfmark and qBittorrent.");
        }

        var best = files.Where(EbookFormats.IsBook).OrderBy(EbookFormats.RankOf).ThenByDescending(Length).FirstOrDefault();
        if (best is not null)
        {
            return new Found(best, null, null);
        }

        foreach (var archive in files.Where(EbookFormats.IsZip).OrderByDescending(Length))
        {
            if (BestEntry(archive) is { } entry)
            {
                return new Found(archive, archive, entry);
            }
        }

        if (files.Any(EbookFormats.IsRar))
        {
            throw new EbookImportException(
                $"The download {source} is a RAR archive, which Pufferfin does not open. Shelfmark extracts RAR archives itself when its \"Hardlink Book Torrents\" setting is off.");
        }

        var formats = files.Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant()).Where(e => e.Length > 0).Distinct(StringComparer.Ordinal).Take(5);
        throw new EbookImportException(
            $"The download {source} holds no book Pufferfin files ({string.Join(", ", EbookFormats.Filed)}). It holds: {string.Join(", ", formats)}.");
    }

    private static string? BestEntry(string archive)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            return zip.Entries
                .Where(e => EbookFormats.IsBook(e.FullName) && e.Length is > 0 and <= MaxExtractedBytes)
                .OrderBy(e => EbookFormats.RankOf(e.FullName))
                .ThenByDescending(e => e.Length)
                .Select(e => e.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new EbookImportException($"The archive {archive} cannot be opened: {ex.Message}");
        }
    }

    private static long Length(string path) => new FileInfo(path).Length;

    private static async Task ExtractAsync(string archive, string entryName, string target, CancellationToken cancellationToken)
    {
        // Extracted already: a file there is this book, the folder holding one book file only.
        if (File.Exists(target))
        {
            return;
        }

        // The name of the target is Pufferfin's, never the entry's: a path in the archive cannot leave the folder.
        var partial = target + ".partial";
        try
        {
            var zip = await ZipFile.OpenReadAsync(archive, cancellationToken).ConfigureAwait(false);
            await using (zip.ConfigureAwait(false))
            {
                var entry = zip.GetEntry(entryName) ?? throw new EbookImportException($"The archive {archive} no longer holds {entryName}.");
                var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                    await using (output.ConfigureAwait(false))
                    {
                        await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            File.Move(partial, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private void Link(string path, string target, string folder)
    {
        if (!_links.IsSupported)
        {
            throw new EbookImportException("This platform cannot create hard links: books are imported on Linux and Windows only.");
        }

        if (File.Exists(target))
        {
            // Imported already, or the download was filed here directly.
            if (_links.GetIdentity(target) is { } existing && _links.GetIdentity(path) is { } downloaded && existing.IsSameFile(downloaded))
            {
                return;
            }

            throw new EbookImportException($"Another file already exists in the library at {target}.");
        }

        if (!_links.TryCreateHardLink(path, target))
        {
            throw new EbookImportException(
                $"A hard link of {path} could not be created in {folder}. The downloads and the library must be on the same file system, and Pufferfin must be able to write in the library.");
        }
    }

    private sealed record Found(string Path, string? Archive, string? EntryName);
}
