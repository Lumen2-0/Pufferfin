using System.Collections.Generic;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// The outcome of the choice of an audiobook release.
/// </summary>
/// <param name="Release">The release to download, or <c>null</c> when none may be taken.</param>
/// <param name="Ranked">Every release, best first, complete editions included.</param>
/// <param name="Problem">Why no release was picked.</param>
public sealed record AudiobookSelection(ShelfmarkRelease? Release, IReadOnlyList<RankedAudiobookRelease> Ranked, RequestProblem? Problem);
