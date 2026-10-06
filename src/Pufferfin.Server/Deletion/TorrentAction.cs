namespace Pufferfin.Server.Deletion;

/// <summary>
/// What a deletion does to one torrent of the title.
/// </summary>
public enum TorrentAction
{
    /// <summary>
    /// The torrent and its downloaded files are removed now.
    /// </summary>
    RemoveNow,

    /// <summary>
    /// The torrent keeps seeding, waiting for release, until its minimum seeding time; then the hourly task purges it.
    /// </summary>
    KeepUntilMinimum,

    /// <summary>
    /// Another title of the library still uses the torrent: it stays under the seeding policy.
    /// </summary>
    KeepShared
}
