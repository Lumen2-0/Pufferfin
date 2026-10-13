namespace Pufferfin.Server.Requests;

/// <summary>
/// What the expiry does with a request, at a given time.
/// </summary>
public enum TemporaryDecision
{
    /// <summary>
    /// Not a temporary film, not available yet, or already handled: nothing to do.
    /// </summary>
    None,

    /// <summary>
    /// Temporary and available, but its time is not up yet.
    /// </summary>
    NotDue,

    /// <summary>
    /// Its time is up, but another request wants the film to stay: it is kept.
    /// </summary>
    Keep,

    /// <summary>
    /// Its time is up: the film and its torrent are removed.
    /// </summary>
    Remove
}
