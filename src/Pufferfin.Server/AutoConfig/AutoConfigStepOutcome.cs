namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// The outcome of an auto-configuration step.
/// </summary>
/// <param name="Status">The status.</param>
/// <param name="Detail">A short explanation written by the step. Never a secret, a URL or a remote message.</param>
public sealed record AutoConfigStepOutcome(AutoConfigStepStatus Status, string? Detail = null);
