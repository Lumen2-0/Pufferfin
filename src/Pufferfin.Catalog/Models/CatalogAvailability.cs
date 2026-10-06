namespace Pufferfin.Catalog.Models;

/// <summary>
/// Whether a catalog entry is present in the local library.
/// </summary>
public enum CatalogAvailability
{
    /// <summary>The entry is absent from the local library.</summary>
    Missing,

    /// <summary>The entry is in the local library.</summary>
    OnDisk
}
