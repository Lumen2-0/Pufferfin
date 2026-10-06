namespace Pufferfin.Server.Storage;

/// <summary>
/// Where a torrent stands, for the storage screen.
/// </summary>
public enum SeedState
{
    /// <summary>
    /// Still downloading.
    /// </summary>
    Downloading,

    /// <summary>
    /// Seeding, protected until its minimum seeding time.
    /// </summary>
    Protected,

    /// <summary>
    /// Seeding past its minimum, until the target ratio or the maximum age.
    /// </summary>
    Seeding,

    /// <summary>
    /// Its title was deleted, but it keeps seeding until its minimum: waiting for release.
    /// </summary>
    WaitingForRelease,

    /// <summary>
    /// Due for a purge at the next hourly cycle (or as soon as its import is confirmed).
    /// </summary>
    DueForPurge
}
