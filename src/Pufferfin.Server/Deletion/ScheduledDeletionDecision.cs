namespace Pufferfin.Server.Deletion;

/// <summary>
/// What a run does with a scheduled deletion.
/// </summary>
public enum ScheduledDeletionDecision
{
    /// <summary>
    /// Already handled: never looked at again.
    /// </summary>
    Done,

    /// <summary>
    /// Its time has not come.
    /// </summary>
    NotDue,

    /// <summary>
    /// Deleted now.
    /// </summary>
    Delete
}
