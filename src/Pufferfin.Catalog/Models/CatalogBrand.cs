namespace Pufferfin.Catalog.Models;

/// <summary>
/// A brand Discover can filter by: a streaming service, a studio or a network.
/// </summary>
/// <param name="Key">Its key (<c>netflix</c>), passed back as the <c>brand</c> of Discover.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind"><c>Service</c>, <c>Studio</c> or <c>Network</c>.</param>
/// <param name="LogoUrl">Its logo: the app icon of a service, the wordmark of a studio or a network.</param>
public sealed record CatalogBrand(string Key, string Name, string Kind, string LogoUrl);
