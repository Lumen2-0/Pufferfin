using System.Collections.Generic;

namespace Pufferfin.Server.Requests.Integration;

/// <summary>
/// The Jellyfin library, as the Requests module sees it: is a title there yet, and a nudge to scan it.
/// </summary>
public interface IMediaLibrary
{
    /// <summary>
    /// Gets a value indicating whether a film is in the library.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <returns><c>true</c> when the film is there.</returns>
    bool HasMovie(int tmdbId);

    /// <summary>
    /// Gets a value indicating whether every given episode of a series is in the library.
    /// </summary>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <param name="tvdbId">The TVDB id of the series, if known.</param>
    /// <param name="episodes">The episodes, in the Sonarr numbering.</param>
    /// <returns><c>true</c> when all of them are there.</returns>
    bool HasEpisodes(int tmdbId, int? tvdbId, IReadOnlyCollection<RequestEpisode> episodes);

    /// <summary>
    /// Asks the library to pick up a new file or folder: the path itself when the server sees it, else a library scan,
    /// at most once every few minutes.
    /// </summary>
    /// <param name="path">The path Radarr or Sonarr imported to, if known.</param>
    void Refresh(string? path);

    /// <summary>
    /// Finds the film Radarr imported to a folder when the library identified it as another film (a folder named after
    /// its title alone, "The Hunt (2012)", can be matched to another "The Hunt"), and gives it back its TMDB id, its
    /// metadata being read again for it.
    /// </summary>
    /// <param name="tmdbId">The TMDB id of the film asked for.</param>
    /// <param name="arrFolder">The folder of the film in Radarr; only its name is compared, the paths differ.</param>
    /// <returns><c>true</c> when the film is in the library under that folder.</returns>
    bool ClaimMovie(int tmdbId, string? arrFolder);
}
