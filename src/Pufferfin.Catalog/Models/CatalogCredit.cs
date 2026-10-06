namespace Pufferfin.Catalog.Models;

/// <summary>
/// A cast or crew member of a title.
/// </summary>
public sealed class CatalogCredit
{
    /// <summary>
    /// Gets the TMDB person id.
    /// </summary>
    public required int TmdbId { get; init; }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the character for the cast, the job for the crew.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Gets the department.
    /// </summary>
    public string? Department { get; init; }

    /// <summary>
    /// Gets the portrait URL.
    /// </summary>
    public string? ImageUrl { get; init; }
}
