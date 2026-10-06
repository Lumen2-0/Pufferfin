namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Whether a release is in the wanted language. In ranking order.
/// </summary>
public enum LanguageMatch
{
    /// <summary>
    /// The release is in the wanted language.
    /// </summary>
    Same,

    /// <summary>
    /// Neither Shelfmark nor the release title says.
    /// </summary>
    Unknown,

    /// <summary>
    /// The release is in another language.
    /// </summary>
    Other
}
