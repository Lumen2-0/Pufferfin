namespace Pufferfin.Server.Storage;

/// <summary>
/// The kinds of storage advice.
/// </summary>
public enum StorageAdviceCode
{
    /// <summary>
    /// The library and the downloads are on different disks or mounts: seeding takes its space twice.
    /// </summary>
    HardlinksImpossible,

    /// <summary>
    /// The hard link check could not run.
    /// </summary>
    HardlinksUnknown,

    /// <summary>
    /// A disk reached <c>STORAGE_WARNING_PERCENT</c>.
    /// </summary>
    DiskAlmostFull,

    /// <summary>
    /// A directory does not exist on this server.
    /// </summary>
    DirectoryMissing,

    /// <summary>
    /// qBittorrent could not be reached: the seeding state is missing.
    /// </summary>
    QBittorrentUnavailable
}
