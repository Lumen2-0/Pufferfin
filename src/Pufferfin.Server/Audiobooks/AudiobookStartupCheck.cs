using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Checks the audiobook setup once at startup and writes every problem to the log. The administration dashboard
/// runs the same checks on demand.
/// </summary>
public sealed class AudiobookStartupCheck : BackgroundService
{
    /// <summary>
    /// How long after startup the checks run: the auto-configuration creates the qBittorrent category first.
    /// </summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IPufferfinConfigurationStore _configuration;
    private readonly AudiobookHealthCheck _check;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AudiobookStartupCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookStartupCheck"/> class.
    /// </summary>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="check">The audiobook checks.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AudiobookStartupCheck(IPufferfinConfigurationStore configuration, AudiobookHealthCheck check, TimeProvider timeProvider, ILogger<AudiobookStartupCheck> logger)
    {
        _configuration = configuration;
        _check = check;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs the checks and logs the outcome.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    public async Task<AudiobookHealthReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        var report = await _check.RunAsync(_configuration.Current.Settings, cancellationToken).ConfigureAwait(false);
        if (!report.Enabled)
        {
            _logger.LogInformation("Audiobooks are off: Shelfmark is not configured");
            return report;
        }

        foreach (var check in report.Checks)
        {
            if (check.Status == AudiobookCheckStatus.Problem)
            {
                _logger.LogWarning("Audiobooks, {Check}: {Message} {Detail}", check.Code, check.Message, check.Detail);
            }
        }

        if (!report.HasProblems)
        {
            _logger.LogInformation("Audiobooks: Shelfmark, qBittorrent and the library folder are ready");
        }

        return report;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // A failed check must not take the server down: it is a diagnosis.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError("The audiobook startup check failed ({ErrorType})", ex.GetType().Name);
        }
    }
}
