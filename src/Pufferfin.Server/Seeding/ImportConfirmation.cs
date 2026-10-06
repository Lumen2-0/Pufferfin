using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// Tells whether the library already holds what a torrent downloaded, so purging the torrent with its files loses
/// nothing. When in doubt, the answer is no: the torrent waits for the next cycle.
/// </summary>
/// <remarks>
/// A torrent of Radarr or Sonarr is imported once it left their queue. Any other torrent (audiobooks) is imported
/// once each of its files has a second hard link, in the library. One instance serves one cycle: the queues are
/// read once.
/// </remarks>
public sealed class ImportConfirmation
{
    private readonly ArrClient _arr;
    private readonly QBittorrentClient _qBittorrent;
    private readonly IFileLinks _links;
    private readonly PufferfinSettings _settings;
    private readonly StoragePaths _paths;
    private readonly ILogger _logger;
    private readonly Dictionary<PufferfinService, HashSet<string>?> _queues = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportConfirmation"/> class.
    /// </summary>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="links">The inodes and hard links.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="logger">The logger.</param>
    public ImportConfirmation(ArrClient arr, QBittorrentClient qBittorrent, IFileLinks links, PufferfinSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _arr = arr;
        _qBittorrent = qBittorrent;
        _links = links;
        _settings = settings;
        _paths = StoragePaths.From(settings);
        _logger = logger;
    }

    /// <summary>
    /// Tells whether the library holds what a torrent downloaded.
    /// </summary>
    /// <param name="torrent">The torrent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> only when the import is confirmed.</returns>
    public async Task<bool> IsImportedAsync(QBittorrentTorrent torrent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        if (string.IsNullOrEmpty(torrent.Hash))
        {
            return false;
        }

        PufferfinService? arr = torrent.Category switch
        {
            var c when string.Equals(c, MediaLayout.RadarrCategory, StringComparison.OrdinalIgnoreCase) => PufferfinService.Radarr,
            var c when string.Equals(c, MediaLayout.SonarrCategory, StringComparison.OrdinalIgnoreCase) => PufferfinService.Sonarr,
            _ => null
        };

        if (arr is PufferfinService service)
        {
            var queue = await QueueAsync(service, cancellationToken).ConfigureAwait(false);
            return queue is not null && !queue.Contains(torrent.Hash);
        }

        return await IsHardLinkedAsync(torrent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HashSet<string>?> QueueAsync(PufferfinService service, CancellationToken cancellationToken)
    {
        if (_queues.TryGetValue(service, out var cached))
        {
            return cached;
        }

        HashSet<string>? queue = null;
        if (_settings.GetService(service) is { } settings)
        {
            try
            {
                var items = await _arr.GetQueueAsync(settings, cancellationToken).ConfigureAwait(false);
                queue = items.Select(i => i.DownloadId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (ArrApiException ex)
            {
                _logger.LogWarning("The queue of {Service} is unavailable, its torrents are kept this cycle: {Failure}", service, ex.Message);
            }
        }

        _queues[service] = queue;
        return queue;
    }

    private async Task<bool> IsHardLinkedAsync(QBittorrentTorrent torrent, CancellationToken cancellationToken)
    {
        if (!_links.IsSupported || string.IsNullOrEmpty(torrent.SavePath) || _settings.GetService(PufferfinService.QBittorrent) is not { } qBittorrent)
        {
            return false;
        }

        IReadOnlyList<QBittorrentFile> files;
        try
        {
            files = await _qBittorrent.GetFilesAsync(qBittorrent, torrent.Hash!, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            _logger.LogWarning("The files of a torrent are unavailable, it is kept this cycle: {Failure}", ex.Message);
            return false;
        }

        var downloaded = files.Where(f => f.Priority > 0 && !string.IsNullOrEmpty(f.Name)).ToList();
        return downloaded.Count > 0
            && downloaded.All(f => _links.GetIdentity(_paths.ToLocalPath(Path.Join(torrent.SavePath, f.Name))) is { LinkCount: > 1 });
    }
}
