using System;
using System.Collections.Generic;

namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// The result of one auto-configuration run.
/// </summary>
/// <param name="Trigger">What triggered the run.</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="CompletedAt">When the run completed.</param>
/// <param name="Steps">The outcome of each step, in order.</param>
public sealed record AutoConfigRunResult(
    AutoConfigTrigger Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<AutoConfigStepReport> Steps);
