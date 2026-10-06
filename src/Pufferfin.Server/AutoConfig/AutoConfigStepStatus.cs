namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// The outcome of an auto-configuration step.
/// </summary>
public enum AutoConfigStepStatus
{
    /// <summary>
    /// The step created what was missing.
    /// </summary>
    Applied,

    /// <summary>
    /// Everything was already in place.
    /// </summary>
    Unchanged,

    /// <summary>
    /// The step does not apply, for example because its service is not configured.
    /// </summary>
    Skipped,

    /// <summary>
    /// The step failed. The other steps still run.
    /// </summary>
    Failed
}
