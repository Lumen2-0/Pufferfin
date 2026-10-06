using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// Runs the registered <see cref="IAutoConfigStep"/> instances, one run at a time.
/// </summary>
/// <remarks>
/// The runner itself changes nothing outside the steps: replaying it is idempotent as long as each
/// step honours the <see cref="IAutoConfigStep"/> contract.
/// </remarks>
public sealed class AutoConfigRunner : IAutoConfigRunner, IDisposable
{
    /// <summary>
    /// The default time limit for one step.
    /// </summary>
    public static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<IAutoConfigStep> _steps;
    private readonly IPufferfinConfigurationStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutoConfigRunner> _logger;
    private readonly TimeSpan _stepTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AutoConfigRunResult? _lastRun;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoConfigRunner"/> class.
    /// </summary>
    /// <param name="steps">The steps, in registration order.</param>
    /// <param name="store">The configuration store.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AutoConfigRunner(IEnumerable<IAutoConfigStep> steps, IPufferfinConfigurationStore store, TimeProvider timeProvider, ILogger<AutoConfigRunner> logger)
        : this(steps, store, timeProvider, logger, DefaultStepTimeout)
    {
    }

    internal AutoConfigRunner(IEnumerable<IAutoConfigStep> steps, IPufferfinConfigurationStore store, TimeProvider timeProvider, ILogger<AutoConfigRunner> logger, TimeSpan stepTimeout)
    {
        _steps = steps.ToList();
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
        _stepTimeout = stepTimeout;
    }

    /// <inheritdoc />
    public AutoConfigRunResult? LastRun => Volatile.Read(ref _lastRun);

    /// <inheritdoc />
    public async Task<AutoConfigRunResult> RunAsync(AutoConfigTrigger trigger, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read the settings once the gate is held, so a run always sees the latest save.
            var settings = _store.Current.Settings;
            var startedAt = _timeProvider.GetUtcNow();
            var reports = new List<AutoConfigStepReport>(_steps.Count);

            foreach (var step in _steps)
            {
                reports.Add(await RunStepAsync(step, settings, cancellationToken).ConfigureAwait(false));
            }

            var result = new AutoConfigRunResult(trigger, startedAt, _timeProvider.GetUtcNow(), reports);
            Volatile.Write(ref _lastRun, result);
            _logger.LogInformation(
                "Pufferfin auto-configuration ({Trigger}) completed: {StepCount} steps, {FailedCount} failed",
                trigger,
                reports.Count,
                reports.Count(r => r.Status == AutoConfigStepStatus.Failed));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<AutoConfigStepReport> RunStepAsync(IAutoConfigStep step, PufferfinSettings settings, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_stepTimeout);
        try
        {
            var outcome = await step.ApplyAsync(settings, timeoutSource.Token).ConfigureAwait(false);
            return new AutoConfigStepReport(step.Name, outcome.Status, outcome.Detail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Pufferfin auto-configuration step {Step} timed out", step.Name);
            return new AutoConfigStepReport(step.Name, AutoConfigStepStatus.Failed, "The step timed out.");
        }
#pragma warning disable CA1031 // A failing step must not stop the others.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Exception messages can quote a URL holding a key: report and log the type only.
            _logger.LogError("Pufferfin auto-configuration step {Step} failed with {ErrorType}", step.Name, ex.GetType().Name);
            return new AutoConfigStepReport(step.Name, AutoConfigStepStatus.Failed, string.Concat("The step failed (", ex.GetType().Name, ")."));
        }
    }
}
