namespace Pufferfin.Server.Storage;

/// <summary>
/// A directory the storage screen reports on.
/// </summary>
public enum StorageRole
{
    /// <summary>
    /// The library (<c>MEDIA_PATH</c>).
    /// </summary>
    Media,

    /// <summary>
    /// The downloads (<c>DOWNLOADS_PATH</c>).
    /// </summary>
    Downloads
}
