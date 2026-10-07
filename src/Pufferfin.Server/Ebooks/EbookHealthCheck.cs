using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;
using Pufferfin.Server.Requests.Integration;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Checks the ebook setup: the Shelfmark key, the <c>books</c> category of qBittorrent, the library folder, the Jellyfin
/// library of type <c>books</c> that indexes it, and whether hard links are possible.
/// </summary>
public sealed class EbookHealthCheck
{
    private readonly IServiceConnectionTester _tester;
    private readonly QBittorrentClient _qBittorrent;
    private readonly EbookLibrary _library;
    private readonly ILibraryFolders _libraries;
    private readonly HardlinkCheck _hardlinks;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EbookHealthCheck"/> class.
    /// </summary>
    /// <param name="tester">The connection tester.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="library">The book library.</param>
    /// <param name="libraries">The folders of the Jellyfin libraries.</param>
    /// <param name="hardlinks">The hard link check of the storage screen.</param>
    /// <param name="timeProvider">The time provider.</param>
    public EbookHealthCheck(IServiceConnectionTester tester, QBittorrentClient qBittorrent, EbookLibrary library, ILibraryFolders libraries, HardlinkCheck hardlinks, TimeProvider timeProvider)
    {
        _tester = tester;
        _qBittorrent = qBittorrent;
        _library = library;
        _libraries = libraries;
        _hardlinks = hardlinks;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Runs the checks. Every call has a time limit; a service that cannot be reached is reported, never waited on.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report. Books off when Shelfmark is not configured, with no check.</returns>
    public async Task<EbookHealthReport> RunAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsEnabled(PufferfinService.Shelfmark))
        {
            return new EbookHealthReport(false, [], _timeProvider.GetUtcNow());
        }

        var checks = new List<EbookCheck> { await ShelfmarkAsync(settings, cancellationToken).ConfigureAwait(false) };
        checks.Add(await CategoryAsync(settings, cancellationToken).ConfigureAwait(false));
        checks.Add(LibraryFolder());
        checks.Add(JellyfinLibrary());
        checks.Add(SameFileSystem(settings));
        return new EbookHealthReport(true, checks, _timeProvider.GetUtcNow());
    }

    private static bool IsUnder(string path, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(full, root, StringComparison.Ordinal)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private async Task<EbookCheck> ShelfmarkAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        var result = await _tester.TestAsync(PufferfinService.Shelfmark, settings, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            ConnectionStatus.Connected => new EbookCheck(EbookCheckCode.ShelfmarkConnection, AudiobookCheckStatus.Ok, "Shelfmark answers and accepts the key."),
            ConnectionStatus.KeyRejected => new EbookCheck(
                EbookCheckCode.ShelfmarkConnection,
                AudiobookCheckStatus.Problem,
                "Shelfmark refused the API key. Set SHELFMARK_API_KEY to the key Shelfmark was started with."),
            _ => new EbookCheck(EbookCheckCode.ShelfmarkConnection, AudiobookCheckStatus.Problem, "Shelfmark cannot be reached.", result.Detail)
        };
    }

    private async Task<EbookCheck> CategoryAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        if (settings.GetService(PufferfinService.QBittorrent) is not { } service)
        {
            return new EbookCheck(
                EbookCheckCode.QBittorrentCategory,
                AudiobookCheckStatus.Skipped,
                "qBittorrent is not configured: its books category cannot be checked.");
        }

        try
        {
            var categories = await _qBittorrent.GetCategoriesAsync(service, cancellationToken).ConfigureAwait(false);
            if (!categories.ContainsKey(MediaLayout.BooksCategory))
            {
                return new EbookCheck(
                    EbookCheckCode.QBittorrentCategory,
                    AudiobookCheckStatus.Problem,
                    "qBittorrent has no books category. Shelfmark must send the ebooks to it: set its QBITTORRENT_CATEGORY to books, and its QBITTORRENT_CATEGORY_AUDIOBOOK to audiobooks so the two never mix.");
            }

            return new EbookCheck(EbookCheckCode.QBittorrentCategory, AudiobookCheckStatus.Ok, "qBittorrent has the books category.");
        }
        catch (ArrApiException ex)
        {
            return new EbookCheck(EbookCheckCode.QBittorrentCategory, AudiobookCheckStatus.Skipped, "qBittorrent cannot be read: " + ex.Message);
        }
    }

    private EbookCheck LibraryFolder()
    {
        try
        {
            // The import creates it anyway; creating it here tells right away whether it can be.
            Directory.CreateDirectory(_library.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new EbookCheck(EbookCheckCode.LibraryFolder, AudiobookCheckStatus.Problem, "The library folder of the books cannot be created.", _library.Root);
        }

        return AudiobookLibrary.CanWriteIn(_library.Root)
            ? new EbookCheck(EbookCheckCode.LibraryFolder, AudiobookCheckStatus.Ok, "Pufferfin can write in the library folder of the books.", _library.Root)
            : new EbookCheck(
                EbookCheckCode.LibraryFolder,
                AudiobookCheckStatus.Problem,
                "Pufferfin cannot write in the library folder of the books: no book can be imported. Check its owner and permissions.",
                _library.Root);
    }

    private EbookCheck JellyfinLibrary()
    {
        var root = _library.Root;
        return _libraries.GetBookLibraryFolders().Any(folder => IsUnder(root, folder))
            ? new EbookCheck(EbookCheckCode.JellyfinLibrary, AudiobookCheckStatus.Ok, "A Jellyfin library of type Books indexes the library folder of the books.", root)
            : new EbookCheck(
                EbookCheckCode.JellyfinLibrary,
                AudiobookCheckStatus.Problem,
                "No Jellyfin library of type Books indexes the library folder of the books: imported books will not show. Add a Books library with this folder.",
                root);
    }

    private EbookCheck SameFileSystem(PufferfinSettings settings)
    {
        // The probe of the storage screen: a real link from the downloads into the library, kept an hour.
        var result = _hardlinks.Check(StoragePaths.From(settings));
        var status = result.Status switch
        {
            HardlinkStatus.Possible => AudiobookCheckStatus.Ok,
            HardlinkStatus.Impossible => AudiobookCheckStatus.Problem,
            _ => AudiobookCheckStatus.Skipped
        };
        var message = result.Status == HardlinkStatus.Impossible
            ? result.Message + " Books cannot be imported until the downloads and the library are on one file system."
            : result.Message;
        return new EbookCheck(EbookCheckCode.SameFileSystem, status, message);
    }
}
