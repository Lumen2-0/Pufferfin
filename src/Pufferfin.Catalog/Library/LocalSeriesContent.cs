using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// The seasons and episodes of a series present in the local library, as one user sees them.
/// </summary>
public sealed class LocalSeriesContent
{
    /// <summary>
    /// Gets the content of a series absent from the library.
    /// </summary>
    public static LocalSeriesContent Empty { get; } = new();

    /// <summary>
    /// Gets the library item id of the series, or <c>null</c> when the series is not in the library.
    /// </summary>
    public Guid? SeriesItemId { get; init; }

    /// <summary>
    /// Gets the season folders. A number may appear twice when the series sits in two libraries.
    /// </summary>
    public IReadOnlyList<LocalSeasonEntry> Seasons { get; init; } = [];

    /// <summary>
    /// Gets the episodes with a file on disk: virtual (missing) episodes are left out.
    /// </summary>
    public IReadOnlyList<LocalEpisodeEntry> Episodes { get; init; } = [];
}
