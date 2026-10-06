using System.Collections.Generic;
using Pufferfin.Server.Arr;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Builds the outcome of a step from what it created.
/// </summary>
internal static class StepOutcomes
{
    /// <summary>
    /// Gets the outcome of a step that created the given items, or nothing.
    /// </summary>
    /// <param name="created">What was created, in words.</param>
    /// <returns>The outcome.</returns>
    public static AutoConfigStepOutcome FromCreated(IReadOnlyCollection<string> created)
        => created.Count == 0
            ? new AutoConfigStepOutcome(AutoConfigStepStatus.Unchanged)
            : new AutoConfigStepOutcome(AutoConfigStepStatus.Applied, "Created " + string.Join(", ", created) + ".");

    /// <summary>
    /// Gets the outcome of a step whose service call failed. The detail is the exception message, which never quotes a URL or a key.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="created">What was created before the failure.</param>
    /// <returns>The outcome.</returns>
    public static AutoConfigStepOutcome Failed(ArrApiException exception, IReadOnlyCollection<string>? created = null)
        => new(
            AutoConfigStepStatus.Failed,
            created is { Count: > 0 } ? exception.Message + " Created before the failure: " + string.Join(", ", created) + "." : exception.Message);

    /// <summary>
    /// Gets the outcome of a step whose service is not configured.
    /// </summary>
    /// <param name="service">The service name.</param>
    /// <returns>The outcome.</returns>
    public static AutoConfigStepOutcome NotConfigured(string service)
        => new(AutoConfigStepStatus.Skipped, service + " is not configured.");
}
