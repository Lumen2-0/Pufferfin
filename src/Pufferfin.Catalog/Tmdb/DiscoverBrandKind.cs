namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// What a Discover brand filters by.
/// </summary>
public enum DiscoverBrandKind
{
    /// <summary>
    /// A streaming service: the titles its subscription offers.
    /// </summary>
    Service,

    /// <summary>
    /// A studio: the titles it produced.
    /// </summary>
    Studio,

    /// <summary>
    /// A television network: the series it aired. No film.
    /// </summary>
    Network
}
