namespace Pufferfin.Server.Deletion;

/// <summary>
/// How a title is deleted. <see cref="Safe"/> is the default value, so a caller that says nothing gets it.
/// </summary>
public enum DeletionMode
{
    /// <summary>
    /// The library files go now; a private torrent keeps seeding until its minimum seeding time, then is purged.
    /// A public torrent, or one past its minimum, goes now.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Everything goes now, private torrents included, even before their minimum: this can get the tracker account
    /// banned for hit and run. Needs an explicit confirmation, and an administrator.
    /// </summary>
    Immediate = 1
}
