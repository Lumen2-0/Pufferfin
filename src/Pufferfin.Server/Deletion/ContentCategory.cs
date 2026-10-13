namespace Pufferfin.Server.Deletion;

/// <summary>
/// The library a title of the content manager belongs to, as the client filters them.
/// </summary>
public enum ContentCategory
{
    /// <summary>
    /// A film of the films library.
    /// </summary>
    Movie,

    /// <summary>
    /// A series of the series library.
    /// </summary>
    Series,

    /// <summary>
    /// A film or a series of the anime library.
    /// </summary>
    Anime,

    /// <summary>
    /// A film or a series of the documentaries library.
    /// </summary>
    Documentary,

    /// <summary>
    /// An audiobook.
    /// </summary>
    Audiobook
}
