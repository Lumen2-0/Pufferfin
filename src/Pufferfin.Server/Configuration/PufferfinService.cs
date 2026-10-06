namespace Pufferfin.Server.Configuration;

/// <summary>
/// The external services Pufferfin can drive.
/// </summary>
public enum PufferfinService
{
    /// <summary>
    /// Radarr, for movies.
    /// </summary>
    Radarr,

    /// <summary>
    /// Sonarr, for series.
    /// </summary>
    Sonarr,

    /// <summary>
    /// Prowlarr, for indexers.
    /// </summary>
    Prowlarr,

    /// <summary>
    /// qBittorrent, the download client.
    /// </summary>
    QBittorrent,

    /// <summary>
    /// Bazarr, for subtitles.
    /// </summary>
    Bazarr,

    /// <summary>
    /// Shelfmark, for audiobooks.
    /// </summary>
    Shelfmark,

    /// <summary>
    /// TMDB, for the movie and series catalog.
    /// </summary>
    Tmdb,

    /// <summary>
    /// FlareSolverr, for indexers protected by Cloudflare.
    /// </summary>
    FlareSolverr
}
