using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// One step of the auto-configuration of the services (for example: declare qBittorrent in Radarr).
/// </summary>
/// <remarks>
/// Steps are replayed at startup and after every configuration save, so each step must be idempotent:
/// read the remote state, create only what is missing, and never modify or delete an item that already
/// exists, since an administrator may have adjusted it by hand. Running a step twice in a row must leave
/// the remote services in the same state, and the second run must report <see cref="AutoConfigStepStatus.Unchanged"/>.
/// A step whose service is not configured returns <see cref="AutoConfigStepStatus.Skipped"/>.
/// </remarks>
public interface IAutoConfigStep
{
    /// <summary>
    /// Gets the step name, shown to administrators.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies the step.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken);
}
