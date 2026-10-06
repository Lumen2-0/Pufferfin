using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Connectivity;

/// <summary>
/// Tests the connection to a configured service, on demand.
/// </summary>
public interface IServiceConnectionTester
{
    /// <summary>
    /// Tests the connection to a service with its saved settings. Never takes longer than the test timeout.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="settings">The settings to test with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    Task<ConnectionTestResult> TestAsync(PufferfinService service, PufferfinSettings settings, CancellationToken cancellationToken);
}
