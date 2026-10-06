namespace Pufferfin.Server.Seeding;

/// <summary>
/// What the seeding policy decides for a torrent.
/// </summary>
public enum SeedVerdict
{
    /// <summary>
    /// Still downloading: never purged by the policy.
    /// </summary>
    Downloading,

    /// <summary>
    /// Seeding, and protected until its minimum seeding time is reached, whatever its ratio.
    /// </summary>
    Protected,

    /// <summary>
    /// Past its minimum, seeding until the target ratio or the maximum age.
    /// </summary>
    Seeding,

    /// <summary>
    /// Past its minimum and at the target ratio: purged.
    /// </summary>
    PurgeTargetRatio,

    /// <summary>
    /// Past its minimum and its maximum age: purged, even though the target ratio is not reached.
    /// </summary>
    PurgeMaxAge,

    /// <summary>
    /// Its title was deleted with the safe mode, and its minimum is now reached: purged.
    /// </summary>
    PurgeReleased
}
