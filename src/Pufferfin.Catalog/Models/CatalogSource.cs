namespace Pufferfin.Catalog.Models;

/// <summary>
/// A source queried by the catalog.
/// </summary>
public enum CatalogSource
{
    /// <summary>The local Jellyfin library.</summary>
    Library,

    /// <summary>The Movie Database.</summary>
    Tmdb,

    /// <summary>The Audible catalog.</summary>
    Audible,

    /// <summary>The Letterboxd diary of the configured user.</summary>
    Letterboxd,

    /// <summary>The book metadata provider of Shelfmark (Hardcover, Open Library or Google Books).</summary>
    Shelfmark
}
