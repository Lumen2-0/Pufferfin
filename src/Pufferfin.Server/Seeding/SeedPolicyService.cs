using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// Runs the seeding policy every hour, on the server, from the persisted schedule of <see cref="SeedPolicyRunner"/>.
/// </summary>
public sealed class SeedPolicyService : BackgroundService
{
    /// <summary>
    /// The longest sleep between two looks at the schedule, so a clock change or a manual run is noticed.
    /// </summary>
    public static readonly TimeSpan MaxSleep = TimeSpan.FromMinutes(5);

    private readonly SeedPolicyRunner _runner;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SeedPolicyService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeedPolicyService"/> class.
    /// </summary>
    /// <param name="runner">The runner.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public SeedPolicyService(SeedPolicyRunner runner, TimeProvider timeProvider, ILogger<SeedPolicyService> logger)
    {
        _runner = runner;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _runner.RunCycleIfDueAsync(stoppingToken).ConfigureAwait(false);
                var wait = _runner.NextCycleDueAt() - _timeProvider.GetUtcNow();
                await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait > MaxSleep ? MaxSleep : wait, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive anything a cycle throws: it is what keeps the seeding policy applied.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "A cycle of the Pufferfin seeding policy failed");
                await Task.Delay(SeedPolicyRunner.RetryDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
