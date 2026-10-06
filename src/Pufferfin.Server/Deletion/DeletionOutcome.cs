namespace Pufferfin.Server.Deletion;

/// <summary>
/// The outcome of a deletion or of its preview.
/// </summary>
/// <param name="Status">How it ended.</param>
/// <param name="Message">An English sentence.</param>
/// <param name="Plan">What the deletion does or did, when the title was found.</param>
public sealed record DeletionOutcome(DeletionStatus Status, string Message, DeletionPlan? Plan = null);
