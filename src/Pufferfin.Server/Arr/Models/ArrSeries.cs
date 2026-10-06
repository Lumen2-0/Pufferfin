namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// A series in Sonarr, reduced to what Pufferfin reads.
/// </summary>
public sealed class ArrSeries
{
    /// <summary>Gets or sets the Sonarr id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the TVDB id.</summary>
    public int TvdbId { get; set; }

    /// <summary>Gets or sets a value indicating whether the series is monitored.</summary>
    public bool Monitored { get; set; }

    /// <summary>Gets or sets the quality profile id.</summary>
    public int QualityProfileId { get; set; }

    /// <summary>Gets or sets the folder of the series.</summary>
    public string? Path { get; set; }
}
