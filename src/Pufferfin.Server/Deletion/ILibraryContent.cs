using System;
using System.Collections.Generic;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// The Jellyfin library, as the deletion sees it.
/// </summary>
public interface ILibraryContent
{
    /// <summary>
    /// Finds a title by Jellyfin item id.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="visibleTo">
    /// The user who must be able to see the item, or <c>null</c> for an administrator, who sees everything.
    /// An item outside the libraries of the user, or an unknown user, finds nothing.
    /// </param>
    /// <returns>The title, or <c>null</c> when the item is not a film, a series, a season, an episode or an audiobook the user can see.</returns>
    LibraryTitle? Find(Guid itemId, Guid? visibleTo);

    /// <summary>
    /// Finds the films of the library with a TMDB id.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <returns>Their Jellyfin item ids; none when the film is not there.</returns>
    IReadOnlyList<Guid> FindMovies(int tmdbId);

    /// <summary>
    /// Deletes the files of a title the server owns (an audiobook) and removes it from the library.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    void DeleteFiles(Guid itemId);

    /// <summary>
    /// Lists every whole title on disk: the films, the series and the audiobooks, for the content manager.
    /// </summary>
    /// <returns>The titles; none for a library that cannot list them.</returns>
    IReadOnlyList<LibraryEntry> ListTitles() => [];
}
