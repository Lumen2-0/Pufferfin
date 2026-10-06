using System;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// A season folder of the local library.
/// </summary>
public sealed class LocalSeasonEntry
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
    /// Gets the display name.
    /// </summary>
    public string? Name { get; init; }
}
