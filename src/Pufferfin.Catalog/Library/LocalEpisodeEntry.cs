using System;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// An episode of the local library, reduced to what the catalog needs to match it against TMDB.
/// </summary>
public sealed class LocalEpisodeEntry
{
    /// <summary>
    /// Gets the library item id.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the season number (0 for specials).
    /// </summary>
    public required int SeasonNumber { get; init; }

    /// <summary>
    /// Gets the episode number, or <c>null</c> when the file name gave none.
    /// </summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>
    /// Gets the last episode number of a file holding several episodes (<c>S01E01-E02</c>).
    /// </summary>
    public int? EpisodeNumberEnd { get; init; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the overview.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the air date.
    /// </summary>
    public DateOnly? AirDate { get; init; }

    /// <summary>
    /// Gets the runtime in minutes.
    /// </summary>
    public int? RuntimeMinutes { get; init; }
}
