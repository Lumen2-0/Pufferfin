namespace Pufferfin.Catalog.Models;

/// <summary>
/// The status of one source in a catalog response.
/// </summary>
/// <param name="Source">The source.</param>
/// <param name="Status">The outcome.</param>
public sealed record CatalogSourceReport(CatalogSource Source, CatalogSourceStatus Status);
