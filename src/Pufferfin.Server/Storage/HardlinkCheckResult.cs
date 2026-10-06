namespace Pufferfin.Server.Storage;

/// <summary>
/// The result of a hard link check.
/// </summary>
/// <param name="Status">Whether hard links work.</param>
/// <param name="Message">An English sentence for the dashboard.</param>
public sealed record HardlinkCheckResult(HardlinkStatus Status, string Message);
