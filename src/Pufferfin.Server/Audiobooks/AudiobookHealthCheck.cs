using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Checks the audiobook setup: the Shelfmark key, the <c>audiobooks</c> category of qBittorrent, the library folder,
/// whether Pufferfin can change and delete the files there, and whether hard links are possible.
/// </summary>
public sealed class AudiobookHealthCheck
{
    private const int MaxReportedPaths = 5;

    private readonly IServiceConnectionTester _tester;
    private readonly QBittorrentClient _qBittorrent;
    private readonly AudiobookLibrary _library;
    private readonly HardlinkCheck _hardlinks;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookHealthCheck"/> class.
    /// </summary>
    /// <param name="tester">The connection tester.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="library">The audiobook library.</param>
    /// <param name="hardlinks">The hard link check of the storage screen.</param>
    /// <param name="timeProvider">The time provider.</param>
    public AudiobookHealthCheck(IServiceConnectionTester tester, QBittorrentClient qBittorrent, AudiobookLibrary library, HardlinkCheck hardlinks, TimeProvider timeProvider)
    {
        _hardlinks = hardlinks;
        _tester = tester;
        _qBittorrent = qBittorrent;
        _library = library;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Runs the checks. Every call has a time limit; a service that cannot be reached is reported, never waited on.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report. Audiobooks off when Shelfmark is not configured, with no check.</returns>
    public async Task<AudiobookHealthReport> RunAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsEnabled(PufferfinService.Shelfmark))
        {
            return new AudiobookHealthReport(false, [], _timeProvider.GetUtcNow());
        }

        var checks = new List<AudiobookCheck> { await ShelfmarkAsync(settings, cancellationToken).ConfigureAwait(false) };
        checks.Add(await CategoryAsync(settings, cancellationToken).ConfigureAwait(false));
        checks.Add(LibraryFolder());
        checks.Add(FilesWritable());
        checks.Add(SameFileSystem(settings));
        return new AudiobookHealthReport(true, checks, _timeProvider.GetUtcNow());
    }

    private static string Paths(IEnumerable<string> paths) => string.Join(" ", paths.Take(MaxReportedPaths));

    private async Task<AudiobookCheck> ShelfmarkAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        var result = await _tester.TestAsync(PufferfinService.Shelfmark, settings, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            ConnectionStatus.Connected => new AudiobookCheck(AudiobookCheckCode.ShelfmarkConnection, AudiobookCheckStatus.Ok, "Shelfmark answers and accepts the key."),
            ConnectionStatus.KeyRejected => new AudiobookCheck(
                AudiobookCheckCode.ShelfmarkConnection,
                AudiobookCheckStatus.Problem,
                "Shelfmark refused the API key. Set SHELFMARK_API_KEY to the key Shelfmark was started with."),
            _ => new AudiobookCheck(AudiobookCheckCode.ShelfmarkConnection, AudiobookCheckStatus.Problem, "Shelfmark cannot be reached.", result.Detail)
        };
    }

    private async Task<AudiobookCheck> CategoryAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        if (settings.GetService(PufferfinService.QBittorrent) is not { } service)
        {
            return new AudiobookCheck(
                AudiobookCheckCode.QBittorrentCategory,
                AudiobookCheckStatus.Skipped,
                "qBittorrent is not configured: its audiobooks category cannot be checked.");
        }

        try
        {
            var categories = await _qBittorrent.GetCategoriesAsync(service, cancellationToken).ConfigureAwait(false);
            if (!categories.ContainsKey(MediaLayout.AudiobooksCategory))
            {
                return new AudiobookCheck(
                    AudiobookCheckCode.QBittorrentCategory,
                    AudiobookCheckStatus.Problem,
                    "qBittorrent has no audiobooks category. Shelfmark must send the audiobooks to it: set its QBITTORRENT_CATEGORY_AUDIOBOOK to audiobooks.");
            }

            return new AudiobookCheck(AudiobookCheckCode.QBittorrentCategory, AudiobookCheckStatus.Ok, "qBittorrent has the audiobooks category.");
        }
        catch (ArrApiException ex)
        {
            return new AudiobookCheck(AudiobookCheckCode.QBittorrentCategory, AudiobookCheckStatus.Skipped, "qBittorrent cannot be read: " + ex.Message);
        }
    }

    private AudiobookCheck LibraryFolder()
    {
        try
        {
            // The import creates it anyway; creating it here tells right away whether it can be.
            Directory.CreateDirectory(_library.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AudiobookCheck(AudiobookCheckCode.LibraryFolder, AudiobookCheckStatus.Problem, "The library folder of the audiobooks cannot be created.", _library.Root);
        }

        return AudiobookLibrary.CanWriteIn(_library.Root)
            ? new AudiobookCheck(AudiobookCheckCode.LibraryFolder, AudiobookCheckStatus.Ok, "Pufferfin can write in the library folder of the audiobooks.", _library.Root)
            : new AudiobookCheck(
                AudiobookCheckCode.LibraryFolder,
                AudiobookCheckStatus.Problem,
                "Pufferfin cannot write in the library folder of the audiobooks: no audiobook can be imported. Check its owner and permissions.",
                _library.Root);
    }

    private AudiobookCheck FilesWritable()
    {
        var notWritable = AudiobookLibrary.FindNotWritable(_library.Root);
        return notWritable.Count == 0
            ? new AudiobookCheck(AudiobookCheckCode.FilesWritable, AudiobookCheckStatus.Ok, "Pufferfin can change and delete the audiobooks of the library.")
            : new AudiobookCheck(
                AudiobookCheckCode.FilesWritable,
                AudiobookCheckStatus.Problem,
                "Pufferfin cannot change or delete some audiobooks of the library, so deleting them from Pufferfin will fail. Give the Pufferfin user write access to them.",
                Paths(notWritable));
    }

    private AudiobookCheck SameFileSystem(PufferfinSettings settings)
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
            ? result.Message + " Audiobooks cannot be imported until the downloads and the library are on one file system."
            : result.Message;
        return new AudiobookCheck(AudiobookCheckCode.SameFileSystem, status, message);
    }
}
