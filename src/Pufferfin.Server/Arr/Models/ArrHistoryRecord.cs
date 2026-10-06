using System;

namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// A history record of Radarr or Sonarr.
/// </summary>
public sealed class ArrHistoryRecord
{
    /// <summary>Gets or sets the record id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the Radarr film id.</summary>
    public int? MovieId { get; set; }

    /// <summary>Gets or sets the Sonarr episode id.</summary>
    public int? EpisodeId { get; set; }

    /// <summary>Gets or sets the event type (<c>grabbed</c>, <c>downloadFolderImported</c>, <c>downloadFailed</c>, <c>downloadIgnored</c>, <c>movieFileDeleted</c>, <c>episodeFileDeleted</c>...).</summary>
    public string? EventType { get; set; }

    /// <summary>Gets or sets when the event happened.</summary>
    public DateTimeOffset Date { get; set; }

    /// <summary>Gets or sets the download client id.</summary>
    public string? DownloadId { get; set; }

    /// <summary>Gets or sets the release title.</summary>
    public string? SourceTitle { get; set; }
}
