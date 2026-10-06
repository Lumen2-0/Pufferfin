namespace Pufferfin.Catalog.Models;

/// <summary>
/// A country of origin or production.
/// </summary>
/// <param name="Code">The ISO 3166-1 code.</param>
/// <param name="Name">The name, when TMDB provides it.</param>
public sealed record CatalogCountry(string Code, string? Name);
