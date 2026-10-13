namespace Pufferfin.Server.Deletion;

/// <summary>
/// The kind of trackers the torrents of a title come from.
/// </summary>
public enum ContentTracker
{
    /// <summary>
    /// No torrent found for the title.
    /// </summary>
    None,

    /// <summary>
    /// Only public trackers.
    /// </summary>
    Public,

    /// <summary>
    /// Only private trackers.
    /// </summary>
    Private,

    /// <summary>
    /// Public and private trackers.
    /// </summary>
    Mixed,

    /// <summary>
    /// At least one torrent of unknown privacy, treated as private.
    /// </summary>
    Unknown
}
