namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// What a check of the audiobook setup looks at.
/// </summary>
public enum AudiobookCheckCode
{
    /// <summary>
    /// Shelfmark answers and accepts the key.
    /// </summary>
    ShelfmarkConnection,

    /// <summary>
    /// The <c>audiobooks</c> category exists in qBittorrent.
    /// </summary>
    QBittorrentCategory,

    /// <summary>
    /// The library folder of the audiobooks exists and Pufferfin can write in it.
    /// </summary>
    LibraryFolder,

    /// <summary>
    /// Pufferfin can change and delete the files of the audiobooks in the library.
    /// </summary>
    FilesWritable,

    /// <summary>
    /// The downloads and the library are on one file system, so hard links are possible.
    /// </summary>
    SameFileSystem
}
