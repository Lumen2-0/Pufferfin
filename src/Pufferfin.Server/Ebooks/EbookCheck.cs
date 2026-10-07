using Pufferfin.Server.Audiobooks;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// One check of the ebook setup.
/// </summary>
/// <param name="Code">What is checked.</param>
/// <param name="Status">The outcome, with the same meaning as for the audiobooks.</param>
/// <param name="Message">An English sentence written by Pufferfin. Never a secret.</param>
/// <param name="Detail">The paths concerned, when there are some.</param>
public sealed record EbookCheck(EbookCheckCode Code, AudiobookCheckStatus Status, string Message, string? Detail = null);
