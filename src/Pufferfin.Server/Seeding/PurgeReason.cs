namespace Pufferfin.Server.Seeding;

/// <summary>
/// Why Pufferfin removed a torrent.
/// </summary>
public enum PurgeReason
{
    /// <summary>
    /// The target ratio was reached after the minimum seeding time.
    /// </summary>
    TargetRatio,

    /// <summary>
    /// The maximum age was reached after the minimum seeding time.
    /// </summary>
    MaxAge,

    /// <summary>
    /// Its title was deleted with the safe mode, and the minimum seeding time is now reached.
    /// </summary>
    Released,

    /// <summary>
    /// Its title was deleted with the safe mode and the torrent is public, or its minimum was already reached.
    /// </summary>
    SafeDeletion,

    /// <summary>
    /// Its title was deleted with the immediate mode, after an explicit confirmation.
    /// </summary>
    ImmediateDeletion
}
