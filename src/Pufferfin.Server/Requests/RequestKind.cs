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
    /// A book to read from a metadata provider of Shelfmark, through Shelfmark. Never fulfilled automatically with a
    /// complete edition, an omnibus or a box set.
    /// </summary>
    Ebook
}
