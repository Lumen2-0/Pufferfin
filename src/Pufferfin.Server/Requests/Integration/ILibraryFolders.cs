using System.Collections.Generic;

namespace Pufferfin.Server.Requests.Integration;

/// <summary>
/// The folders of the Jellyfin libraries.
/// </summary>
public interface ILibraryFolders
{
    /// <summary>
    /// Gets the folders of the Jellyfin libraries of type <c>books</c>.
    /// </summary>
    /// <returns>The folders, as this server sees them.</returns>
    IReadOnlyList<string> GetBookLibraryFolders();
}
