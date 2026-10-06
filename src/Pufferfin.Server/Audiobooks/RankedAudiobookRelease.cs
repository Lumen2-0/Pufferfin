namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// A release of a book, with how it matches the book.
/// </summary>
/// <param name="Release">The release.</param>
/// <param name="TitleMatches">Whether the release title contains the title of the book.</param>
/// <param name="Volume">Whether the release names the volume of the book in its series.</param>
/// <param name="Language">Whether the release is in the wanted language.</param>
/// <param name="IsCompleteEdition">Whether the release holds several books: offered, never picked automatically.</param>
public sealed record RankedAudiobookRelease(ShelfmarkRelease Release, bool TitleMatches, VolumeMatch Volume, LanguageMatch Language, bool IsCompleteEdition)
{
    /// <summary>
    /// Gets a value indicating whether the release may be picked: the right title, no other volume, no other language.
    /// </summary>
    public bool IsAcceptable => TitleMatches && Volume != VolumeMatch.Other && Language != LanguageMatch.Other;
}
