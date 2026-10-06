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
    Episode
}
