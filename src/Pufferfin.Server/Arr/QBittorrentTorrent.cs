using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pufferfin.Server.Arr;

/// <summary>
/// A torrent of qBittorrent, reduced to what the progress needs.
/// </summary>
public sealed class QBittorrentTorrent
{
    /// <summary>
    /// Gets the JSON options of the qBittorrent API, which uses snake case.
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Gets or sets the hash, lower case.</summary>
    public string? Hash { get; set; }

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
}
