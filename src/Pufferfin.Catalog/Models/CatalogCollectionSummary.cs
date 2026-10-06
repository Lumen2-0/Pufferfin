namespace Pufferfin.Catalog.Models;

/// <summary>
/// The collection (saga) a film belongs to.
/// </summary>
/// <param name="TmdbId">The TMDB collection id.</param>
/// <param name="Name">The name.</param>
/// <param name="PosterUrl">The poster URL.</param>
public sealed record CatalogCollectionSummary(int TmdbId, string Name, string? PosterUrl);
