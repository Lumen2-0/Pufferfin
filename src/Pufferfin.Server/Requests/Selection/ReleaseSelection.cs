using Pufferfin.Server.Arr.Models;

namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// The outcome of a release selection: a release and the language it was taken for, or the problem.
/// </summary>
/// <param name="Release">The release, or <c>null</c>.</param>
/// <param name="Language">The language the release was taken for.</param>
/// <param name="Problem">Why no release was taken.</param>
public sealed record ReleaseSelection(ArrRelease? Release, LanguageCandidate? Language, RequestProblem? Problem);
