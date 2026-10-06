using System.Text.Json.Serialization;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// A download in the Shelfmark queue, from <c>/api/status</c>.
/// </summary>
public sealed class ShelfmarkDownload
{
    /// <summary>Gets or sets the id: the source id of the release.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the author.</summary>
    public string? Author { get; set; }

    /// <summary>Gets or sets the content type (<c>audiobook</c>...).</summary>
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    /// <summary>Gets or sets the progress, from 0 to 100.</summary>
    public double Progress { get; set; }

    /// <summary>Gets or sets the status: <c>queued</c>, <c>resolving</c>, <c>locating</c>, <c>downloading</c>, <c>complete</c>, <c>error</c> or <c>cancelled</c>.</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the status message, an error reason for a failed download.</summary>
    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    /// <summary>Gets or sets where Shelfmark put the files of a complete download, as Shelfmark sees the disk.</summary>
    [JsonPropertyName("download_path")]
    public string? DownloadPath { get; set; }

    /// <summary>Gets a value indicating whether the download is still on its way.</summary>
    [JsonIgnore]
    public bool IsActive => Status is "queued" or "resolving" or "locating" or "downloading";

    /// <summary>Gets a value indicating whether the download is complete.</summary>
    [JsonIgnore]
    public bool IsComplete => Status == "complete";

    /// <summary>Gets a value indicating whether the download failed.</summary>
    [JsonIgnore]
    public bool IsFailed => Status == "error";

    /// <summary>Gets a value indicating whether the download was cancelled in Shelfmark.</summary>
    [JsonIgnore]
    public bool IsCancelled => Status == "cancelled";
}
