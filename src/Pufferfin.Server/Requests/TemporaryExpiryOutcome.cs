namespace Pufferfin.Server.Requests;

/// <summary>
/// What the expiry of a temporary film did.
/// </summary>
public enum TemporaryExpiryOutcome
{
    /// <summary>
    /// The film left the library and Radarr. Its public torrent was removed; a private one keeps seeding until its
    /// minimum seeding time, then the hourly task removes it with its files.
    /// </summary>
    Removed,

    /// <summary>
    /// The film was no longer in the library when it expired: nothing was deleted.
    /// </summary>
    AlreadyGone,

    /// <summary>
    /// Someone else asked for the film to stay (another request for it, not temporary): nothing was deleted.
    /// </summary>
    Kept,

    /// <summary>
    /// The library, Radarr or qBittorrent did not match what the request downloaded: nothing was deleted, an
    /// administrator decides.
    /// </summary>
    NeedsAttention
}
