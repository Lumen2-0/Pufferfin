using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Checks the ebook setup once at startup and writes every problem to the log. The administration dashboard runs the
/// same checks on demand.
/// </summary>
public sealed class EbookStartupCheck : BackgroundService
{
    private readonly IPufferfinConfigurationStore _configuration;
    private readonly EbookHealthCheck _check;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EbookStartupCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EbookStartupCheck"/> class.
    /// </summary>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="check">The ebook checks.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public EbookStartupCheck(IPufferfinConfigurationStore configuration, EbookHealthCheck check, TimeProvider timeProvider, ILogger<EbookStartupCheck> logger)
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
    public async Task<EbookHealthReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        var report = await _check.RunAsync(_configuration.Current.Settings, cancellationToken).ConfigureAwait(false);
        if (!report.Enabled)
        {
            _logger.LogInformation("Books are off: Shelfmark is not configured");
            return report;
        }

        foreach (var check in report.Checks)
        {
            if (check.Status == AudiobookCheckStatus.Problem)
            {
                _logger.LogWarning("Books, {Check}: {Message} {Detail}", check.Code, check.Message, check.Detail);
            }
        }

        if (!report.HasProblems)
        {
            _logger.LogInformation("Books: Shelfmark, qBittorrent, the library folder and its Jellyfin library are ready");
        }

        return report;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // After the auto-configuration, which creates the qBittorrent category.
            await Task.Delay(AudiobookStartupCheck.StartupDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
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
            _logger.LogError("The ebook startup check failed ({ErrorType})", ex.GetType().Name);
        }
    }
}
