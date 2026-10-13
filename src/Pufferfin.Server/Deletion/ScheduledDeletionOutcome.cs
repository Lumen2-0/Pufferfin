namespace Pufferfin.Server.Deletion;

/// <summary>
/// What became of a scheduled deletion.
/// </summary>
public enum ScheduledDeletionOutcome
{
    /// <summary>
    /// The title was deleted; its private torrents keep seeding until their minimum.
    /// </summary>
    Removed,

    /// <summary>
    /// The title was no longer in the library: nothing was deleted.
    /// </summary>
    AlreadyGone,

    /// <summary>
    /// The title no longer matched what was scheduled, or the deletion refused: nothing was deleted.
    /// </summary>
    NeedsAttention
}
