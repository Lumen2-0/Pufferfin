using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Finds the qBittorrent torrent of an audiobook of the library. Audiobooks have no Radarr or Sonarr history to
/// link a title to its torrent: the files of the library are hard links of the torrent files, so they share their
/// inodes, and that is the link.
/// </summary>
public sealed class AudiobookTorrentLocator
{
    private readonly QBittorrentClient _qBittorrent;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookTorrentLocator"/> class.
    /// </summary>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    public AudiobookTorrentLocator(QBittorrentClient qBittorrent)
    {
        _qBittorrent = qBittorrent;
    }

    /// <summary>
    /// Finds the torrent whose files share an inode with the files of a book folder.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="bookFolder">The folder of the book in the library.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The torrent hash, or <c>null</c> when no torrent of the <c>audiobooks</c> category holds these files.</returns>
    /// <exception cref="ArrApiException">qBittorrent could not be read.</exception>
    public async Task<string?> FindAsync(ServiceSettings service, string bookFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(bookFolder);
        var identities = Identities(bookFolder);
        if (identities.Count == 0)
        {
            return null;
        }

        var torrents = await _qBittorrent.GetTorrentsInCategoryAsync(service, MediaLayout.AudiobooksCategory, cancellationToken).ConfigureAwait(false);
        foreach (var torrent in torrents.Where(t => t.Hash is not null && t.SavePath is not null))
        {
            var files = await _qBittorrent.GetTorrentFilesAsync(service, torrent.Hash!, cancellationToken).ConfigureAwait(false);
            foreach (var file in files)
            {
                var path = Path.Combine(torrent.SavePath!, file.Replace('/', Path.DirectorySeparatorChar));
                if (FileIdentity.Of(path) is { } identity && identities.Contains(identity))
                {
                    return torrent.Hash!.ToLowerInvariant();
                }
            }
        }

        return null;
    }

    private static HashSet<FileIdentity> Identities(string folder)
    {
        var identities = new HashSet<FileIdentity>();
        if (!Directory.Exists(folder))
        {
            return identities;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(AudiobookLibrary.IsAudio))
        {
            if (FileIdentity.Of(file) is { } identity)
            {
                identities.Add(identity);
            }
        }

        return identities;
    }
}
