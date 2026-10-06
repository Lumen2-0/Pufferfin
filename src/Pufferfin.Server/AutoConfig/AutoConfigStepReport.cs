namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// The outcome of one step in a run.
/// </summary>
/// <param name="Name">The step name.</param>
/// <param name="Status">The status.</param>
/// <param name="Detail">A short explanation.</param>
public sealed record AutoConfigStepReport(string Name, AutoConfigStepStatus Status, string? Detail);
