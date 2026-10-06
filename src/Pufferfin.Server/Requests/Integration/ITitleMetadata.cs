using System.Threading;
using System.Threading.Tasks;

namespace Pufferfin.Server.Requests.Integration;

/// <summary>
/// What the Requests module needs to know about a title, from TMDB.
/// </summary>
public interface ITitleMetadata
{
    /// <summary>
    /// Gets a film.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The film, or <c>null</c> when TMDB does not know it.</returns>
    Task<TitleMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a series, with its TVDB id.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The series, or <c>null</c> when TMDB does not know it.</returns>
    Task<TitleMetadata?> GetSeriesAsync(int tmdbId, CancellationToken cancellationToken);
}
