namespace Pufferfin.Server.Deletion;

/// <summary>
/// How a deletion ended.
/// </summary>
public enum DeletionStatus
{
    /// <summary>
    /// The plan is ready; nothing was deleted (a preview).
    /// </summary>
    Planned,

    /// <summary>
    /// The title was deleted. Torrents kept until their minimum are waiting for release.
    /// </summary>
    Deleted,

    /// <summary>
    /// The immediate mode was asked without the confirmation. Nothing was deleted.
    /// </summary>
    ConfirmationRequired,

    /// <summary>
    /// No such title in the library.
    /// </summary>
    NotFound,

    /// <summary>
    /// The user may not delete titles, or may not use the immediate mode.
    /// </summary>
    Forbidden,

    /// <summary>
    /// Radarr or Sonarr, which holds the title, is not configured. Nothing was deleted.
    /// </summary>
    ServiceDisabled,

    /// <summary>
    /// A service could not be reached, or the files could not be deleted. Nothing was deleted.
    /// </summary>
    ServiceUnavailable
}
