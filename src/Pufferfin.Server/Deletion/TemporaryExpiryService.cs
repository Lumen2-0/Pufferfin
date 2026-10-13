using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// Runs the expiry of the temporary films every few minutes, on the server.
/// </summary>
public sealed class TemporaryExpiryService : BackgroundService
{
    /// <summary>
    /// The time between two runs.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The time after the start before the first run, so the library is loaded.
    /// </summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);

    private readonly TemporaryExpiryRunner _runner;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TemporaryExpiryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemporaryExpiryService"/> class.
    /// </summary>
    /// <param name="runner">The runner.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public TemporaryExpiryService(TemporaryExpiryRunner runner, TimeProvider timeProvider, ILogger<TemporaryExpiryService> logger)
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
                    _logger.LogError(ex, "A run of the Pufferfin temporary film expiry failed");
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
