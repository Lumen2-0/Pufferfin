namespace Pufferfin.Server.Ebooks;

/// <summary>
/// What a check of the ebook setup looks at.
/// </summary>
public enum EbookCheckCode
{
    /// <summary>
    /// Shelfmark answers and accepts the key.
    /// </summary>
    ShelfmarkConnection,

    /// <summary>
    /// The <c>books</c> category exists in qBittorrent, apart from the <c>audiobooks</c> one.
    /// </summary>
    QBittorrentCategory,

    /// <summary>
    /// The library folder of the books exists and Pufferfin can write in it.
    /// </summary>
    LibraryFolder,

    /// <summary>
    /// A Jellyfin library of type <c>books</c> covers the library folder of the books, so they are indexed.
    /// </summary>
    JellyfinLibrary,

    /// <summary>
    /// The downloads and the library are on one file system, so hard links are possible.
    /// </summary>
    SameFileSystem
}
