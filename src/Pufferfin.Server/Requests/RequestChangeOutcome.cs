namespace Pufferfin.Server.Requests;

/// <summary>
/// What happened to a change on a request.
/// </summary>
public enum RequestChangeOutcome
{
    /// <summary>
    /// The change was made.
    /// </summary>
    Done,

    /// <summary>
    /// The request does not exist, or belongs to another user.
    /// </summary>
    NotFound,

    /// <summary>
    /// Only an administrator may make this change.
    /// </summary>
    Forbidden,

    /// <summary>
    /// The request is not in a state that allows the change.
    /// </summary>
    InvalidState
}
