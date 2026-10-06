namespace Pufferfin.Server.Arr;

/// <summary>
/// The library a title is filed in, which decides its root folder.
/// </summary>
public enum MediaCategory
{
    /// <summary>
    /// A film or a series that is neither an anime nor a documentary.
    /// </summary>
    Standard,

    /// <summary>
    /// An anime: Japanese animation.
    /// </summary>
    Anime,

    /// <summary>
    /// A documentary.
    /// </summary>
    Documentary
}
