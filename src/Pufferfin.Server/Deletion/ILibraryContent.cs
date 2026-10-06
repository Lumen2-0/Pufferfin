using System;

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
    /// <returns>The title, or <c>null</c> when the item is not a film, a series, a season, an episode or an audiobook.</returns>
    LibraryTitle? Find(Guid itemId);

    /// <summary>
    /// Deletes the files of a title the server owns (an audiobook) and removes it from the library.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    void DeleteFiles(Guid itemId);
}
