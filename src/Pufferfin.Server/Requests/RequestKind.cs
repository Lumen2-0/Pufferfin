namespace Pufferfin.Server.Requests;

/// <summary>
/// What a request asks for.
/// </summary>
public enum RequestKind
{
    /// <summary>
    /// A film, through Radarr.
    /// </summary>
    Movie,

    /// <summary>
    /// A whole season of a series, through Sonarr.
    /// </summary>
    Season,

    /// <summary>
    /// One episode of a series, through Sonarr. Never fulfilled with a season pack.
    /// </summary>
    Episode,

    /// <summary>
    /// An audiobook from the Audible catalog, through Shelfmark. Never fulfilled automatically with a complete edition.
    /// </summary>
    Audiobook,

    /// <summary>
    /// A book to read. No longer offered: kept only so the requests made before still load. A new one is refused and
    /// no workflow handles the old ones.
    /// </summary>
    Ebook
}
