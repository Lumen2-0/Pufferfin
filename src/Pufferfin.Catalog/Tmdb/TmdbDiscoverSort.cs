namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// The order of a Discover list.
/// </summary>
public enum TmdbDiscoverSort
{
    /// <summary>
    /// Most popular first.
    /// </summary>
    Popular,

    /// <summary>
    /// Best rated first, among titles with enough votes for the rating to mean something.
    /// </summary>
    TopRated,

    /// <summary>
    /// Latest released first, among titles already out.
    /// </summary>
    Newest
}
