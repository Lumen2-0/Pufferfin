namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// A release of a book, with how it matches the book.
/// </summary>
/// <param name="Release">The release.</param>
/// <param name="Title">How the release title matches the title of the book.</param>
/// <param name="Volume">Whether the release names the volume of the book in its series.</param>
/// <param name="Language">Whether the release is in the wanted language.</param>
/// <param name="IsCompleteEdition">Whether the release holds several books: offered, never picked automatically.</param>
public sealed record RankedAudiobookRelease(ShelfmarkRelease Release, TitleMatch Title, VolumeMatch Volume, LanguageMatch Language, bool IsCompleteEdition)
{
    /// <summary>
    /// Gets a value indicating whether the release may be picked: exactly the title, no other volume, no other language.
    /// </summary>
    public bool IsAcceptable => Title == TitleMatch.Exact && Volume != VolumeMatch.Other && Language != LanguageMatch.Other;
}
