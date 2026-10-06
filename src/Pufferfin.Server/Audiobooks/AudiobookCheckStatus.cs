namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// The outcome of a check of the audiobook setup.
/// </summary>
public enum AudiobookCheckStatus
{
    /// <summary>
    /// All is well.
    /// </summary>
    Ok,

    /// <summary>
    /// Something needs an administrator; the message says what.
    /// </summary>
    Problem,

    /// <summary>
    /// Not checked: what it needs is not configured or cannot be reached.
    /// </summary>
    Skipped
}
