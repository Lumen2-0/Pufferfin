namespace Pufferfin.Server.Seeding;

/// <summary>
/// Whether a cycle of the seeding policy ran.
/// </summary>
public enum SeedCycleStatus
{
    /// <summary>
    /// The cycle ran to its end.
    /// </summary>
    Completed,

    /// <summary>
    /// Nothing to do: qBittorrent is not configured.
    /// </summary>
    Skipped,

    /// <summary>
    /// The cycle stopped because qBittorrent could not be reached. Nothing was purged; the next cycle comes sooner.
    /// </summary>
    Failed
}
