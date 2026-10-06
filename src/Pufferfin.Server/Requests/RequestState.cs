namespace Pufferfin.Server.Requests;

/// <summary>
/// Where a request stands. The usual path is
/// <see cref="Queued"/>, <see cref="Searching"/>, <see cref="Downloading"/>, <see cref="Importing"/>, <see cref="Available"/>.
/// </summary>
public enum RequestState
{
    /// <summary>
    /// Waiting for an administrator to approve it.
    /// </summary>
    PendingApproval,

    /// <summary>
    /// Waiting for a download slot (<c>MAX_CONCURRENT_DOWNLOADS</c>), in a queue order administrators can change.
    /// </summary>
    Queued,

    /// <summary>
    /// The server is adding the title to Radarr or Sonarr and searching for a release.
    /// </summary>
    Searching,

    /// <summary>
    /// No release yet. Not an error: the title stays monitored, and the server searches again later.
    /// </summary>
    WaitingForSources,

    /// <summary>
    /// A release is downloading.
    /// </summary>
    Downloading,

    /// <summary>
    /// The download is complete and being imported into the library.
    /// </summary>
    Importing,

    /// <summary>
    /// The title is in the library.
    /// </summary>
    Available,

    /// <summary>
    /// The request failed. <see cref="MediaRequest.Problem"/> always says why. It can be retried.
    /// </summary>
    Failed,

    /// <summary>
    /// Cancelled by its owner or an administrator.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Refused by an administrator.
    /// </summary>
    Rejected
}
