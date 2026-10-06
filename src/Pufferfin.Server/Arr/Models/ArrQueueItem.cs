using System.Collections.Generic;

namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// An item of the download queue of Radarr or Sonarr.
/// </summary>
public sealed class ArrQueueItem
{
    /// <summary>Gets or sets the queue id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Radarr film id.</summary>
    public int? MovieId { get; set; }

    /// <summary>Gets or sets the Sonarr series id.</summary>
    public int? SeriesId { get; set; }

    /// <summary>Gets or sets the Sonarr episode id.</summary>
    public int? EpisodeId { get; set; }

    /// <summary>Gets or sets the release title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the download status (<c>downloading</c>, <c>paused</c>, <c>queued</c>, <c>completed</c>, <c>warning</c>...).</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the tracked download status (<c>ok</c>, <c>warning</c>, <c>error</c>).</summary>
    public string? TrackedDownloadStatus { get; set; }

    /// <summary>Gets or sets the tracked download state (<c>downloading</c>, <c>importPending</c>, <c>importBlocked</c>, <c>importing</c>, <c>imported</c>, <c>failedPending</c>, <c>failed</c>...).</summary>
    public string? TrackedDownloadState { get; set; }

    /// <summary>Gets or sets the status messages.</summary>
    public IList<ArrStatusMessage> StatusMessages { get; set; } = [];

    /// <summary>Gets or sets the error message.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the size in bytes.</summary>
    public double Size { get; set; }

    /// <summary>Gets or sets the bytes left.</summary>
    public double Sizeleft { get; set; }

    /// <summary>Gets or sets the estimated time left, as a .NET time span string.</summary>
    public string? Timeleft { get; set; }

    /// <summary>Gets or sets the download client id: the torrent hash for qBittorrent.</summary>
    public string? DownloadId { get; set; }
}
