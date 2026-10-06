namespace Pufferfin.Catalog.Models;

/// <summary>
/// A trailer or teaser.
/// </summary>
public sealed class CatalogVideo
{
    /// <summary>
    /// Gets the video title.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the hosting site (<c>YouTube</c>, <c>Vimeo</c>).
    /// </summary>
    public required string Site { get; init; }

    /// <summary>
    /// Gets the video key on the hosting site.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the video type (<c>Trailer</c>, <c>Teaser</c>).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the ISO 639-1 language of the video.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets a value indicating whether the video is official.
    /// </summary>
    public bool Official { get; init; }

    /// <summary>
    /// Gets the watch URL, when the site is known.
    /// </summary>
    public string? Url { get; init; }
}
