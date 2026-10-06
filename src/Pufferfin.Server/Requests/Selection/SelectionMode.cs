namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// The kind of release wanted.
/// </summary>
public enum SelectionMode
{
    /// <summary>
    /// A film.
    /// </summary>
    Movie,

    /// <summary>
    /// One episode: a season pack never qualifies.
    /// </summary>
    Episode,

    /// <summary>
    /// A whole season, as a season pack.
    /// </summary>
    SeasonPack
}
