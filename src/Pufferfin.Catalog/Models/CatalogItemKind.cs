namespace Pufferfin.Catalog.Models;

/// <summary>
/// The kind of a catalog entry.
/// </summary>
public enum CatalogItemKind
{
    /// <summary>A film.</summary>
    Movie,

    /// <summary>A TV series.</summary>
    Series,

    /// <summary>A person (actor, director...).</summary>
    Person,

    /// <summary>An audiobook.</summary>
    Audiobook
}
