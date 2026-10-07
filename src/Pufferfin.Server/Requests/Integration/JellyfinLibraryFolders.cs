using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Pufferfin.Server.Requests.Integration;

/// <summary>
/// <see cref="ILibraryFolders"/> backed by the virtual folders of the Jellyfin library manager.
/// </summary>
public sealed class JellyfinLibraryFolders : ILibraryFolders
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLibraryFolders"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public JellyfinLibraryFolders(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetBookLibraryFolders()
        => _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType == CollectionTypeOptions.books)
            .SelectMany(f => f.Locations)
            .ToList();
}
