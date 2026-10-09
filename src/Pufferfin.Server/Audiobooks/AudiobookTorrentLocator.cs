using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Deletion;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Finds the qBittorrent torrent of an audiobook of the library. Audiobooks have no Radarr or Sonarr history to
/// link a title to its torrent: the files of the library are hard links of the torrent files, so they share their
/// inodes, and that is the link. The same matching as the deletion (<see cref="InodeTorrentMatcher"/>).
/// </summary>
public sealed class AudiobookTorrentLocator
{
    private readonly QBittorrentClient _qBittorrent;
    private readonly IFileLinks _links;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookTorrentLocator"/> class.
    /// </summary>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="links">The inodes and hard links.</param>
    public AudiobookTorrentLocator(QBittorrentClient qBittorrent, IFileLinks links)
    {
        _qBittorrent = qBittorrent;
        _links = links;
    }

    /// <summary>
    /// Finds the torrent of the <c>audiobooks</c> category whose files share an inode with the files of a book folder.
    /// </summary>
    /// <param name="settings">The settings: qBittorrent, and the paths to translate its paths into this server's.</param>
    /// <param name="bookFolder">The folder of the book in the library.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The torrent hash, or <c>null</c> when none holds these files, or qBittorrent is not configured.</returns>
    /// <exception cref="ArrApiException">qBittorrent could not be read.</exception>
    public async Task<string?> FindAsync(PufferfinSettings settings, string bookFolder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(bookFolder);
        if (settings.GetService(PufferfinService.QBittorrent) is not { } service || !Directory.Exists(bookFolder))
        {
            return null;
        }

        var identities = Directory.EnumerateFiles(bookFolder, "*", SearchOption.AllDirectories)
            .Where(AudiobookLibrary.IsAudio)
            .Select(_links.GetIdentity)
            .OfType<FileIdentity>()
            .ToList();
        if (identities.Count == 0)
        {
            return null;
        }

        var paths = StoragePaths.From(settings);
        var torrents = (await _qBittorrent.GetAllTorrentsAsync(service, cancellationToken).ConfigureAwait(false))
            .Where(t => !string.IsNullOrEmpty(t.Hash) && !string.IsNullOrEmpty(t.SavePath)
                && string.Equals(t.Category, MediaLayout.AudiobooksCategory, StringComparison.OrdinalIgnoreCase));
        foreach (var torrent in torrents)
        {
            var torrentFiles = await _qBittorrent.GetFilesAsync(service, torrent.Hash!, cancellationToken).ConfigureAwait(false);
            var candidate = (torrent.Hash!.ToLowerInvariant(), (IReadOnlyList<FileIdentity?>)torrentFiles
                .Where(f => !string.IsNullOrEmpty(f.Name))
                .Select(f => _links.GetIdentity(paths.ToLocalPath(Path.Join(torrent.SavePath, f.Name))))
                .ToList());
            if (InodeTorrentMatcher.Match(identities, [candidate]).FirstOrDefault() is { } match)
            {
                return match.Hash;
            }
        }

        return null;
    }
}
