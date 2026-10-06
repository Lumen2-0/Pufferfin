namespace Pufferfin.Server.Requests;

/// <summary>
/// The outcome of a change on a request.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Request">The request after the change, or as it is when the change was not allowed.</param>
public sealed record RequestChange(RequestChangeOutcome Outcome, MediaRequest? Request);
