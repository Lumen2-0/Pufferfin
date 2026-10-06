namespace Pufferfin.Server.Requests;

/// <summary>
/// Why a request failed or waits. The client translates the code; the server also gives an English sentence.
/// </summary>
public enum RequestProblemCode
{
    /// <summary>
    /// Radarr or Sonarr is not configured.
    /// </summary>
    ServiceDisabled,

    /// <summary>
    /// Radarr or Sonarr could not be reached or refused the call.
    /// </summary>
    ServiceUnavailable,

    /// <summary>
    /// TMDB, Radarr or Sonarr does not know the title.
    /// </summary>
    TitleUnknown,

    /// <summary>
    /// TMDB gives no TVDB id for the series, and Sonarr only works with TVDB ids.
    /// </summary>
    TvdbIdMissing,

    /// <summary>
    /// Radarr or Sonarr has no root folder to file the title in.
    /// </summary>
    NoRootFolder,

    /// <summary>
    /// Sonarr has no episode matching the number or the air date (one day either side).
    /// </summary>
    EpisodeNotFound,

    /// <summary>
    /// No release found yet. The request waits for sources.
    /// </summary>
    NoRelease,

    /// <summary>
    /// The indexers could not be reached. The request waits for sources.
    /// </summary>
    IndexersUnavailable,

    /// <summary>
    /// Radarr or Sonarr has no indexer able to search.
    /// </summary>
    NoIndexer,

    /// <summary>
    /// Only season packs were found for a single episode, and a season pack is never taken for one episode.
    /// </summary>
    OnlySeasonPacks,

    /// <summary>
    /// Releases exist, but none in a preferred language nor in the original language.
    /// </summary>
    NoAcceptableLanguage,

    /// <summary>
    /// The download failed, with the reason given by Radarr or Sonarr.
    /// </summary>
    DownloadFailed,

    /// <summary>
    /// Radarr or Sonarr cannot import the download, with the reason it gives. The request keeps being tracked.
    /// </summary>
    ImportBlocked,

    /// <summary>
    /// The download client reports a warning, for example a stalled torrent. The download goes on.
    /// </summary>
    DownloadWarning,

    /// <summary>
    /// Only complete editions (several books in one release) were found for an audiobook, and a complete edition is
    /// never taken automatically. The detail names the best one.
    /// </summary>
    OnlyCompleteEditions,

    /// <summary>
    /// The audiobook is in the library, but Pufferfin cannot change or delete some of its files: deleting it from
    /// Pufferfin would fail. The detail names the first ones.
    /// </summary>
    FilesNotWritable
}
