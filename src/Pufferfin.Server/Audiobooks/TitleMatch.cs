namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// How a release title matches the title of a book. In ranking order.
/// </summary>
public enum TitleMatch
{
    /// <summary>
    /// The title of the book, and nothing else but its author, narrator, series and tags (language, volume, format, year).
    /// </summary>
    Exact,

    /// <summary>
    /// The title of the book among other words: maybe another book (<c>Dune Messiah</c> for <c>Dune</c>). Never picked automatically.
    /// </summary>
    Partial,

    /// <summary>
    /// The title of the book is not in the release title.
    /// </summary>
    None
}
