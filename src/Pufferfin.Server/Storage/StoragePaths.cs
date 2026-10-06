using System;
using System.IO;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Storage;

/// <summary>
/// Where the library and the downloads are, as this server and as qBittorrent see them.
/// </summary>
/// <param name="Media">The library directory, as this server sees it.</param>
/// <param name="Downloads">The downloads directory, as this server sees it.</param>
/// <param name="QBittorrentDownloads">The downloads directory, as qBittorrent sees it.</param>
/// <param name="WarningPercent">The disk usage, in percent, from which the storage screen warns.</param>
public sealed record StoragePaths(string Media, string Downloads, string QBittorrentDownloads, int WarningPercent)
{
    /// <summary>The library directory when <c>MEDIA_PATH</c> is not set.</summary>
    public const string DefaultMediaPath = "/media";

    /// <summary>The downloads directory when <c>DOWNLOADS_PATH</c> is not set.</summary>
    public const string DefaultDownloadsPath = "/downloads";

    /// <summary>The disk usage warning when <c>STORAGE_WARNING_PERCENT</c> is not set.</summary>
    public const int DefaultWarningPercent = 90;

    /// <summary>
    /// Reads the paths from the settings, with their defaults.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The paths.</returns>
    public static StoragePaths From(PufferfinSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var downloads = settings.DownloadsPath ?? DefaultDownloadsPath;
        return new StoragePaths(
            settings.MediaPath ?? DefaultMediaPath,
            downloads,
            settings.QBittorrentDownloadsPath ?? downloads,
            settings.StorageWarningPercent ?? DefaultWarningPercent);
    }

    /// <summary>
    /// Translates a path qBittorrent reported into the path this server sees.
    /// A path outside the qBittorrent downloads directory is returned as it is.
    /// </summary>
    /// <param name="qBittorrentPath">The path, as qBittorrent sees it.</param>
    /// <returns>The path, as this server sees it.</returns>
    public string ToLocalPath(string qBittorrentPath)
    {
        ArgumentNullException.ThrowIfNull(qBittorrentPath);
        var root = QBittorrentDownloads.TrimEnd('/', '\\');
        if (string.Equals(root, Downloads.TrimEnd('/', '\\'), StringComparison.Ordinal))
        {
            return qBittorrentPath;
        }

        if (qBittorrentPath.Length == root.Length && string.Equals(qBittorrentPath, root, StringComparison.Ordinal))
        {
            return Downloads;
        }

        if (qBittorrentPath.StartsWith(root, StringComparison.Ordinal)
            && qBittorrentPath.Length > root.Length
            && qBittorrentPath[root.Length] is '/' or '\\')
        {
            return Path.Join(Downloads, qBittorrentPath[(root.Length + 1)..]);
        }

        return qBittorrentPath;
    }
}
