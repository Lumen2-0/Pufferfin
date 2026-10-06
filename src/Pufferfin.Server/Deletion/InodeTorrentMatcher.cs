using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// Links the files of an audiobook to the torrents they came from, by inode: the library file is a hard link of the
/// downloaded file, so both share their device and inode, whatever their names and folders.
/// </summary>
public static class InodeTorrentMatcher
{
    /// <summary>
    /// Finds the torrents that hold one of the deleted files.
    /// </summary>
    /// <param name="deleted">The identities of the files being deleted.</param>
    /// <param name="torrents">The candidate torrents, with the identity of each downloaded file; <c>null</c> for a file that cannot be read.</param>
    /// <returns>
    /// The matching torrents. A torrent is fully covered when each of its files is either deleted or not linked
    /// anywhere else; otherwise another title of the library still uses it, and it must not be released.
    /// </returns>
    public static IReadOnlyList<InodeMatch> Match(IReadOnlyCollection<FileIdentity> deleted, IEnumerable<(string Hash, IReadOnlyList<FileIdentity?> Files)> torrents)
    {
        ArgumentNullException.ThrowIfNull(deleted);
        ArgumentNullException.ThrowIfNull(torrents);
        var keys = deleted.Select(Key).ToHashSet();
        var matches = new List<InodeMatch>();
        foreach (var (hash, files) in torrents)
        {
            if (!files.Any(f => f is { } identity && keys.Contains(Key(identity))))
            {
                continue;
            }

            // A file shared with the library but not deleted belongs to another title: the torrent stays.
            var fullyCovered = files.All(f => f is { } identity && (keys.Contains(Key(identity)) || identity.LinkCount <= 1));
            matches.Add(new InodeMatch(hash, fullyCovered));
        }

        return matches;
    }

    private static (ulong Device, ulong Inode) Key(FileIdentity identity) => (identity.Device, identity.Inode);
}
