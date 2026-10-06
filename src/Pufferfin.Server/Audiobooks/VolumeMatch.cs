namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Whether a release names the volume of a book in its series. In ranking order.
/// </summary>
public enum VolumeMatch
{
    /// <summary>
    /// The release names the volume of the book.
    /// </summary>
    Same,

    /// <summary>
    /// The release names no volume, or the book is not part of a numbered series.
    /// </summary>
    Unknown,

    /// <summary>
    /// The release names other volumes only: another book of the series.
    /// </summary>
    Other
}
