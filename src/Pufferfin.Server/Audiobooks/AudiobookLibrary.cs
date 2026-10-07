using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Files audiobooks in the library: <c>Author/Title</c> under <see cref="FolderName"/> of the library directory
/// (<c>MEDIA_PATH</c>, <c>/media</c> by default), as hard links of the
/// downloaded files, so the torrent keeps seeding without taking more space, with the Audible cover next to them.
/// </summary>
/// <remarks>
/// Pufferfin sees the disk as Shelfmark and qBittorrent do: the download paths they report must exist at the same
/// path in Pufferfin. Hard links need the downloads and the library on one file system; when they are not, the import
/// stops with that reason rather than copying.
/// </remarks>
public sealed class AudiobookLibrary
{
    /// <summary>
    /// The folder of the audiobooks in the library directory.
    /// </summary>
    public const string FolderName = "audiobooks";

    /// <summary>
    /// The file name of the cover.
    /// </summary>
    public const string CoverFileName = "cover.jpg";

    /// <summary>
    /// The name of the HTTP client that downloads the covers.
    /// </summary>
    public const string HttpClientName = "Pufferfin.Covers";

    /// <summary>
    /// The time limit of a cover download.
    /// </summary>
    public static readonly TimeSpan CoverTimeout = TimeSpan.FromSeconds(15);

    private const int MaxNameLength = 120;
    private const long MaxCoverBytes = 10 * 1024 * 1024;

    private static readonly FrozenSet<string> _audioExtensions = new[]
    {
        ".m4b", ".m4a", ".mp3", ".mp4", ".aac", ".flac", ".ogg", ".oga", ".opus", ".wma", ".wav", ".aax", ".aaxc"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileLinks _links;
    private readonly Func<string> _root;
    private readonly ILogger<AudiobookLibrary> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookLibrary"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="links">The inodes and hard links.</param>
    /// <param name="configuration">The configuration store, for the library directory.</param>
    /// <param name="logger">The logger.</param>
    public AudiobookLibrary(IHttpClientFactory httpClientFactory, IFileLinks links, IPufferfinConfigurationStore configuration, ILogger<AudiobookLibrary> logger)
        : this(httpClientFactory, links, () => Path.Join(StoragePaths.From(configuration.Current.Settings).Media, FolderName), logger)
    {
    }

    internal AudiobookLibrary(IHttpClientFactory httpClientFactory, IFileLinks links, Func<string> root, ILogger<AudiobookLibrary> logger)
    {
        _httpClientFactory = httpClientFactory;
        _links = links;
        _root = root;
        _logger = logger;
    }

    /// <summary>
    /// Gets the library folder of the audiobooks.
    /// </summary>
    public string Root => _root();

    /// <summary>
    /// Gets a value indicating whether a file is an audio file of an audiobook.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><c>true</c> for an audio file.</returns>
    public static bool IsAudio(string path) => _audioExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Gets the folder of a book: <c>Author/Title</c>, each made safe for any file system.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns>The folder.</returns>
    public string FolderOf(AudiobookMetadata book)
    {
        ArgumentNullException.ThrowIfNull(book);
        var author = book.Authors.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "Unknown author";
        return Path.Combine(Root, SafeName(author), SafeName(book.Title));
    }

    /// <summary>
    /// Gets a value indicating whether the library already holds a book: its folder has an audio file.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns><c>true</c> when the book is filed.</returns>
    public bool Contains(AudiobookMetadata book)
    {
        var folder = FolderOf(book);
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(IsAudio);
    }

    /// <summary>
    /// Files a downloaded book: hard links of its audio files in <c>Author/Title</c>, keeping the layout of the
    /// download (one folder per disc stays one folder per disc), then the cover. Running it again is harmless.
    /// </summary>
    /// <param name="source">The downloaded file or folder, as Shelfmark reports it.</param>
    /// <param name="book">The book.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was filed.</returns>
    /// <exception cref="AudiobookImportException">The book cannot be filed, with the reason.</exception>
    public async Task<AudiobookImport> ImportAsync(string source, AudiobookMetadata book, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(book);
        var files = AudioFiles(source);
        if (!_links.IsSupported)
        {
            throw new AudiobookImportException("This platform cannot create hard links: audiobooks are imported on Linux and Windows only.");
        }

        var folder = FolderOf(book);
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var (path, relative) in files)
            {
                var target = Path.Combine(folder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    // Imported already, or the download was filed here directly.
                    if (_links.GetIdentity(target) is { } existing && _links.GetIdentity(path) is { } downloaded && existing.IsSameFile(downloaded))
                    {
                        continue;
                    }

                    throw new AudiobookImportException($"Another file already exists in the library at {target}.");
                }

                if (!_links.TryCreateHardLink(path, target))
                {
                    throw new AudiobookImportException(
                        $"A hard link of {path} could not be created in {folder}. The downloads and the library must be on the same file system, and Pufferfin must be able to write in the library.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AudiobookImportException($"The library folder {folder} cannot be written: {ex.Message}");
        }

        var cover = await WriteCoverAsync(_httpClientFactory, _logger, folder, book.CoverUrl, cancellationToken).ConfigureAwait(false);
        return new AudiobookImport(folder, files.Count, cover, FindNotWritable(folder));
    }

    /// <summary>
    /// Lists what Pufferfin could not change or delete in a folder of the library: the folder itself when no file can
    /// be created in it, and the files it cannot open for writing.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="limit">The number of paths after which the search stops.</param>
    /// <returns>The paths, empty when everything is writable.</returns>
    public static IReadOnlyList<string> FindNotWritable(string folder, int limit = 20)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var found = new List<string>();
        if (!Directory.Exists(folder))
        {
            return found;
        }

        foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).Prepend(folder))
        {
            if (found.Count >= limit)
            {
                return found;
            }

            if (!CanWriteIn(directory))
            {
                found.Add(directory);
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (found.Count >= limit)
            {
                break;
            }

            if (!CanWrite(file))
            {
                found.Add(file);
            }
        }

        return found;
    }

    /// <summary>
    /// Gets a value indicating whether Pufferfin can create, hence delete, a file in a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns><c>true</c> when it can.</returns>
    public static bool CanWriteIn(string directory)
    {
        var probe = Path.Combine(directory, ".pufferfin-write-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes a name safe for any file system: no separator, no reserved character, no trailing dot or space.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The safe name.</returns>
    internal static string SafeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        }

        var safe = builder.ToString();
        if (safe.Length > MaxNameLength)
        {
            safe = safe[..MaxNameLength];
        }

        safe = safe.TrimEnd('.', ' ');
        return safe.Length == 0 || safe == "." || safe == ".." ? "Unknown" : safe;
    }

    private static bool CanWrite(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<(string Path, string Relative)> AudioFiles(string source)
    {
        if (File.Exists(source))
        {
            return IsAudio(source)
                ? [(source, Path.GetFileName(source))]
                : throw new AudiobookImportException($"The download {source} is not an audio file.");
        }

        if (!Directory.Exists(source))
        {
            throw new AudiobookImportException($"The download is not visible from Pufferfin at {source}. Mount the downloads at the same path in Pufferfin as in Shelfmark and qBittorrent.");
        }

        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(IsAudio)
            .Order(StringComparer.Ordinal)
            .Select(f => (f, Path.GetRelativePath(source, f)))
            .ToList();
        return files.Count > 0 ? files : throw new AudiobookImportException($"The download {source} holds no audio file.");
    }

    /// <summary>
    /// Downloads a cover next to the files of a title, once: an existing cover is kept. HTTPS only, size and time limited.
    /// A cover that cannot be downloaded is logged and skipped: it never stops an import.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="folder">The folder of the title.</param>
    /// <param name="coverUrl">The cover.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when the cover is in the folder.</returns>
    internal static async Task<bool> WriteCoverAsync(IHttpClientFactory httpClientFactory, ILogger logger, string folder, string? coverUrl, CancellationToken cancellationToken)
    {
        var path = Path.Combine(folder, CoverFileName);
        if (File.Exists(path))
        {
            return true;
        }

        if (!Uri.TryCreate(coverUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CoverTimeout);
        var partial = path + ".partial";
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxCoverBytes)
            {
                logger.LogWarning("The cover of {Folder} could not be downloaded (HTTP {StatusCode})", folder, (int)response.StatusCode);
                return false;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(partial, bytes, timeout.Token).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The cover of {Folder} did not download within {Timeout}", folder, CoverTimeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("The cover of {Folder} could not be written ({ErrorType})", folder, ex.GetType().Name);
        }

        if (File.Exists(partial))
        {
            File.Delete(partial);
        }

        return false;
    }
}
