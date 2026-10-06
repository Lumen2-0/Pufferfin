using System.Threading;
using System.Threading.Tasks;

namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// Runs the auto-configuration steps.
/// </summary>
public interface IAutoConfigRunner
{
    /// <summary>
    /// Gets the result of the last completed run, or <c>null</c> when none has completed yet.
    /// </summary>
    AutoConfigRunResult? LastRun { get; }

    /// <summary>
    /// Runs every step, in order, with the current settings. Runs never overlap: a call waits for the
    /// previous run to complete. A failing step does not stop the others.
    /// </summary>
    /// <param name="trigger">What triggered the run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    Task<AutoConfigRunResult> RunAsync(AutoConfigTrigger trigger, CancellationToken cancellationToken);
}
