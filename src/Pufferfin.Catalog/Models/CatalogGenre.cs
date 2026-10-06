namespace Pufferfin.Catalog.Models;

/// <summary>
/// A TMDB genre.
/// </summary>
/// <param name="Id">The TMDB genre id.</param>
/// <param name="Name">The localized name.</param>
public sealed record CatalogGenre(int Id, string Name);
