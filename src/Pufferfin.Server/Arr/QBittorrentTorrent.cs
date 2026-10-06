using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pufferfin.Server.Arr;

/// <summary>
/// A torrent of qBittorrent, reduced to what the progress and the seeding policy need.
/// </summary>
/// <remarks>
/// The tracker URL and the magnet link are deliberately not read: on a private tracker they carry the passkey,
/// which must never leave the server, reach a log or be kept in memory longer than the HTTP body.
/// </remarks>
public sealed class QBittorrentTorrent
{
    /// <summary>
    /// Gets the JSON options of the qBittorrent API, which uses snake case.
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Gets or sets the hash, lower case.</summary>
    public string? Hash { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the category.</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the progress, from 0 to 1.</summary>
    public double Progress { get; set; }

    /// <summary>Gets or sets the download speed in bytes per second.</summary>
    [JsonPropertyName("dlspeed")]
    public long DownloadSpeed { get; set; }

    /// <summary>Gets or sets the estimated time left in seconds; 8640000 means unknown.</summary>
    public long Eta { get; set; }

    /// <summary>Gets or sets the state (<c>downloading</c>, <c>stalledDL</c>, <c>metaDL</c>, <c>pausedDL</c>, <c>stoppedDL</c>, <c>uploading</c>, <c>error</c>...).</summary>
    public string? State { get; set; }

    /// <summary>Gets or sets the number of seeds connected.</summary>
    [JsonPropertyName("num_seeds")]
    public int Seeds { get; set; }

    /// <summary>Gets or sets the share ratio.</summary>
    public double Ratio { get; set; }

    /// <summary>Gets or sets the time spent seeding, in seconds. Absent before qBittorrent 4.4.</summary>
    public long? SeedingTime { get; set; }

    /// <summary>Gets or sets when the download completed, in Unix seconds; 0 or less while incomplete.</summary>
    public long CompletionOn { get; set; }

    /// <summary>Gets or sets when the torrent was added, in Unix seconds.</summary>
    public long AddedOn { get; set; }

    /// <summary>Gets or sets the size of the selected files, in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Gets or sets whether the torrent is private. Only reported by recent qBittorrent versions.</summary>
    [JsonPropertyName("private")]
    public bool? IsPrivate { get; set; }

    /// <summary>Gets or sets the save path, as qBittorrent sees it.</summary>
    public string? SavePath { get; set; }

    /// <summary>Gets or sets the content path (the file of a single file torrent, else the root folder), as qBittorrent sees it.</summary>
    public string? ContentPath { get; set; }
}
