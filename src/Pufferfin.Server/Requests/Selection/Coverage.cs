using System.Collections.Generic;

namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// Whether an item (a film or an episode) is already downloaded or on its way.
/// </summary>
/// <param name="Covered">Whether a new download would be a double download.</param>
/// <param name="Reason">Why it is covered: <c>OnDisk</c>, <c>InQueue</c> or <c>GrabbedInHistory</c>; <c>null</c> when not covered.</param>
/// <param name="DownloadIds">The download ids already in flight.</param>
public sealed record Coverage(bool Covered, string? Reason, IReadOnlyList<string> DownloadIds)
{
    /// <summary>
    /// Gets the coverage of an item nothing covers.
    /// </summary>
    public static Coverage None { get; } = new(false, null, []);
}
