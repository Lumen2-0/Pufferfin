namespace Pufferfin.Server.Arr;

/// <summary>
/// A file of a qBittorrent torrent.
/// </summary>
public sealed class QBittorrentFile
{
    /// <summary>Gets or sets the path, relative to the save path of the torrent.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Gets or sets the download priority; 0 means the file is not downloaded.</summary>
    public int Priority { get; set; }
}
