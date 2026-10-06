using System;

namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// An episode in Sonarr.
/// </summary>
public sealed class ArrEpisode
{
    /// <summary>Gets or sets the Sonarr id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the series id.</summary>
    public int SeriesId { get; set; }

    /// <summary>Gets or sets the season number.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number in the season.</summary>
    public int EpisodeNumber { get; set; }

    /// <summary>Gets or sets the local air date, <c>yyyy-MM-dd</c>.</summary>
    public string? AirDate { get; set; }

    /// <summary>Gets or sets the UTC air date.</summary>
    public DateTimeOffset? AirDateUtc { get; set; }

    /// <summary>Gets or sets a value indicating whether the episode is monitored.</summary>
    public bool Monitored { get; set; }

    /// <summary>Gets or sets a value indicating whether the episode has a file on disk.</summary>
    public bool HasFile { get; set; }

    /// <summary>Gets or sets the id of the episode file, 0 when the episode has none.</summary>
    public int EpisodeFileId { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string? Title { get; set; }
}
