using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// Replays the auto-configuration at startup and after every configuration save, in the background,
/// so saving never waits on the services.
/// </summary>
/// <remarks>
/// Saves that arrive during a run are coalesced into a single follow-up run, which reads the latest settings.
/// </remarks>
public sealed class AutoConfigReplayService : BackgroundService
{
    private readonly IPufferfinConfigurationStore _store;
    private readonly IAutoConfigRunner _runner;
    private readonly ILogger<AutoConfigReplayService> _logger;
    private readonly Channel<AutoConfigTrigger> _triggers = Channel.CreateBounded<AutoConfigTrigger>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoConfigReplayService"/> class.
    /// </summary>
    /// <param name="store">The configuration store. Resolving it at startup also applies the environment.</param>
    /// <param name="runner">The auto-configuration runner.</param>
    /// <param name="logger">The logger.</param>
    public AutoConfigReplayService(IPufferfinConfigurationStore store, IAutoConfigRunner runner, ILogger<AutoConfigReplayService> logger)
    {
        _store = store;
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _store.Saved += OnSaved;
        _triggers.Writer.TryWrite(AutoConfigTrigger.Startup);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _store.Saved -= OnSaved;
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var trigger in _triggers.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await _runner.RunAsync(trigger, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Pufferfin auto-configuration replay stopped");
        }
    }

    private void OnSaved(object? sender, PufferfinConfigurationSnapshot snapshot)
        => _triggers.Writer.TryWrite(AutoConfigTrigger.ConfigurationSaved);
}
