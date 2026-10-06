namespace Pufferfin.Server.Storage;

/// <summary>
/// Whether a download can be hard linked into the library.
/// </summary>
public enum HardlinkStatus
{
    /// <summary>
    /// The check could not run: a directory is missing, not writable, or the platform is not supported.
    /// </summary>
    Unknown,

    /// <summary>
    /// Hard links work: seeding takes no extra space.
    /// </summary>
    Possible,

    /// <summary>
    /// Hard links are impossible: the directories are on different disks or mounts. Advice, not an error.
    /// </summary>
    Impossible
}
