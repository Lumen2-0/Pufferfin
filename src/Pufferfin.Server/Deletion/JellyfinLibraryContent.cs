using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// <see cref="ILibraryContent"/> backed by the Jellyfin library manager.
/// </summary>
public sealed class JellyfinLibraryContent : ILibraryContent
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLibraryContent"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public JellyfinLibraryContent(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public LibraryTitle? Find(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        return item switch
        {
            Movie movie => new LibraryTitle(itemId, LibraryTitleKind.Movie, movie.Name, Id(movie, MetadataProvider.Tmdb), null, null, null, []),
            Series series => new LibraryTitle(itemId, LibraryTitleKind.Series, series.Name, Id(series, MetadataProvider.Tmdb), Id(series, MetadataProvider.Tvdb), null, null, []),
            Season { Series: { } series } season when season.IndexNumber is int number => new LibraryTitle(
                itemId,
                LibraryTitleKind.Season,
                string.Create(CultureInfo.InvariantCulture, $"{series.Name} S{number:00}"),
                Id(series, MetadataProvider.Tmdb),
                Id(series, MetadataProvider.Tvdb),
                number,
                null,
                []),
            Episode { Series: { } series } episode when episode.ParentIndexNumber is int season && episode.IndexNumber is int number => new LibraryTitle(
                itemId,
                LibraryTitleKind.Episode,
                string.Create(CultureInfo.InvariantCulture, $"{series.Name} S{season:00}E{number:00}"),
                Id(series, MetadataProvider.Tmdb),
                Id(series, MetadataProvider.Tvdb),
                season,
                number,
                []),
            AudioBook book => new LibraryTitle(itemId, LibraryTitleKind.Audiobook, book.Name, null, null, null, null, Files(book)),
            Folder folder when IsAudiobookFolder(folder) => new LibraryTitle(itemId, LibraryTitleKind.Audiobook, folder.Name, null, null, null, null, Files(folder)),
            _ => null
        };
    }

    /// <inheritdoc />
    public void DeleteFiles(Guid itemId)
    {
        if (_libraryManager.GetItemById(itemId) is { } item)
        {
            _libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true }, notifyParentItem: true);
        }
    }

    private static int? Id(IHasProviderIds item, MetadataProvider provider)
        => item.TryGetProviderId(provider, out var id) && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool IsAudiobookFolder(Folder folder)
    {
        var children = folder.GetRecursiveChildren(i => !i.IsFolder);
        return children.Count > 0 && children.All(c => c is AudioBook);
    }

    private static List<string> Files(BaseItem item)
    {
        var items = item is Folder folder ? folder.GetRecursiveChildren(i => !i.IsFolder) : [item];
        return items.Where(i => !string.IsNullOrEmpty(i.Path) && !i.IsVirtualItem).Select(i => i.Path).Distinct(StringComparer.Ordinal).ToList();
    }
}
