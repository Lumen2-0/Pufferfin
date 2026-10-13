using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// Runs the scheduled deletions a few minutes after the start, then every quarter of an hour.
/// </summary>
public sealed class ScheduledDeletionService : BackgroundService
{
    /// <summary>
    /// The time between two runs.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The time before the first run, so the libraries and the services are up.
    /// </summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(3);

    private readonly ScheduledDeletionRunner _runner;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduledDeletionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledDeletionService"/> class.
    /// </summary>
    /// <param name="runner">The runner.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public ScheduledDeletionService(ScheduledDeletionRunner runner, TimeProvider timeProvider, ILogger<ScheduledDeletionService> logger)
    {
        _runner = runner;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _runner.RunAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
#pragma warning disable CA1031 // The loop must survive anything a run throws: nothing was deleted by a run that failed.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _logger.LogError(ex, "A run of the Pufferfin scheduled deletions failed");
                }

                await Task.Delay(Interval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
    }
}
