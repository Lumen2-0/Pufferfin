namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// One check of the audiobook setup.
/// </summary>
/// <param name="Code">What is checked.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Message">An English sentence written by Pufferfin. Never a secret.</param>
/// <param name="Detail">The paths concerned, when there are some.</param>
public sealed record AudiobookCheck(AudiobookCheckCode Code, AudiobookCheckStatus Status, string Message, string? Detail = null);
