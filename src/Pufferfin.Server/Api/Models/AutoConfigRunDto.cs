using System;
using System.Collections.Generic;
using Pufferfin.Server.AutoConfig;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// The result of an auto-configuration run.
/// </summary>
public sealed class AutoConfigRunDto
{
    /// <summary>
    /// Gets what triggered the run.
    /// </summary>
    public required AutoConfigTrigger Trigger { get; init; }

    /// <summary>
    /// Gets when the run started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets when the run completed.
    /// </summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Gets the outcome of each step, in order.
    /// </summary>
    public required IReadOnlyList<AutoConfigStepReport> Steps { get; init; }

    /// <summary>
    /// Creates the DTO of a run result.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The DTO.</returns>
    public static AutoConfigRunDto From(AutoConfigRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new AutoConfigRunDto
        {
            Trigger = result.Trigger,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            Steps = result.Steps
        };
    }
}
