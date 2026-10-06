namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// An audio language a release may have, in the order they are tried.
/// </summary>
/// <param name="Code">The ISO 639-1 code, or <see cref="MediaRequest.OriginalLanguage"/> for the original version.</param>
public sealed record LanguageCandidate(string Code)
{
    /// <summary>
    /// Gets the candidate of the original version.
    /// </summary>
    public static LanguageCandidate Original { get; } = new(MediaRequest.OriginalLanguage);

    /// <summary>
    /// Gets a value indicating whether the candidate is the original version.
    /// </summary>
    public bool IsOriginal => Code == MediaRequest.OriginalLanguage;
}
