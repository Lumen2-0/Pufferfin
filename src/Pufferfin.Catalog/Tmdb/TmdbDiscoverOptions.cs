namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// What narrows and orders a Discover list beyond the genre and the series types.
/// </summary>
public sealed record TmdbDiscoverOptions
{
    /// <summary>
    /// Gets the order.
    /// </summary>
    public TmdbDiscoverSort Sort { get; init; } = TmdbDiscoverSort.Popular;

    /// <summary>
    /// Gets the year of release (first air year for a series) to keep, or <c>null</c> for any.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the earliest release date (first air date for a series) to keep, or <c>null</c> for any.
    /// </summary>
    public DateOnly? Since { get; init; }

    /// <summary>
    /// Gets the original language to keep (ISO 639-1, such as <c>ko</c> or <c>fr</c>), or <c>null</c> for any.
    /// </summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>
    /// Gets the brand to keep: the titles of a streaming service, a studio or a network; <c>null</c> for any.
    /// </summary>
    public DiscoverBrand? Brand { get; init; }

    /// <summary>
    /// Gets the TMDB keywords to keep, comma delimited, any of them matching (<c>207317,1991</c>: Christmas or Santa Claus), or <c>null</c> for any.
    /// </summary>
    public string? Keywords { get; init; }
}
