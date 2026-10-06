using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests.Workflow;
using Pufferfin.Server.Users;

namespace Pufferfin.Server.Requests;

/// <summary>
/// Runs the requests on the server, whether or not a client is connected: follows the downloads, hands the free
/// slots to the queued requests in queue order, runs the searches, and searches again the requests waiting for sources.
/// </summary>
/// <remarks>
/// <para>
/// At most <c>MAX_CONCURRENT_DOWNLOADS</c> requests (default <see cref="DefaultMaxConcurrentDownloads"/>) search or
/// download at a time; the others wait in the queue. Only this loop moves a request into a slot, so the limit holds
/// whatever the load. A request waiting for sources does not hold a slot.
/// </para>
/// <para>
/// Every change goes through the store with the version the loop read: a request cancelled during a slow search
/// stays cancelled.
/// </para>
/// </remarks>
public sealed class RequestProcessor : BackgroundService, IRequestWaker
{
    /// <summary>
    /// The number of simultaneous downloads when <c>MAX_CONCURRENT_DOWNLOADS</c> is not set.
    /// </summary>
    public const int DefaultMaxConcurrentDownloads = 3;

    /// <summary>
    /// The time between two rounds when nothing wakes the loop.
    /// </summary>
    public static readonly TimeSpan RoundInterval = TimeSpan.FromSeconds(15);

    private readonly RequestStore _store;
    private readonly IPufferfinConfigurationStore _configuration;
    private readonly IPufferfinUserSettingsStore _userSettings;
    private readonly IReadOnlyList<IRequestWorkflow> _workflows;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestProcessor> _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestProcessor"/> class.
    /// </summary>
    /// <param name="store">The request store.</param>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="userSettings">The per user settings.</param>
    /// <param name="workflows">The workflows.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public RequestProcessor(
        RequestStore store,
        IPufferfinConfigurationStore configuration,
        IPufferfinUserSettingsStore userSettings,
        IEnumerable<IRequestWorkflow> workflows,
        TimeProvider timeProvider,
        ILogger<RequestProcessor> logger)
    {
        _store = store;
        _configuration = configuration;
        _userSettings = userSettings;
        _workflows = workflows.ToList();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A round is already due.
        }
    }

    /// <summary>
    /// Runs one round.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RunRoundAsync(CancellationToken cancellationToken)
    {
        var settings = _configuration.Current.Settings;
        var max = settings.MaxConcurrentDownloads ?? DefaultMaxConcurrentDownloads;

        // Follow what is under way first: a finished download frees its slot for this round.
        foreach (var request in _store.GetAll().Where(IsFollowed))
        {
            await StepAsync(request, settings, (workflow, context) => workflow.TrackAsync(request, context, cancellationToken)).ConfigureAwait(false);
        }

        // Hand the free slots out in queue order; requests waiting for sources go after the queue.
        var now = _timeProvider.GetUtcNow();
        var all = _store.GetAll();
        var free = max - all.Count(r => r.TakesSlot);
        var next = all.Where(r => r.State == RequestState.Queued)
            .Concat(all.Where(r => r.State == RequestState.WaitingForSources && r.NextAttemptAt <= now).OrderBy(r => r.NextAttemptAt))
            .Take(Math.Max(0, free));
        foreach (var request in next)
        {
            _store.Update(request.Id, request.Version, r => r.To(RequestState.Searching, now, r.State == RequestState.WaitingForSources ? r.Problem : null));
        }

        foreach (var request in _store.GetAll().Where(r => r.State == RequestState.Searching && (r.NextAttemptAt is null || r.NextAttemptAt <= now)))
        {
            await StepAsync(request, settings, (workflow, context) => workflow.SearchAsync(request, context, cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRoundAsync(stoppingToken).ConfigureAwait(false);
                await _wake.WaitAsync(RoundInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive anything a round throws: it is the only thing moving the requests.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "A round of the Pufferfin request processor failed");
                await Task.Delay(RoundInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsFollowed(MediaRequest request)
        => request.State is RequestState.Downloading or RequestState.Importing or RequestState.WaitingForSources
            || (request.State == RequestState.Failed && request.Problem?.Code == RequestProblemCode.ImportBlocked);

    private async Task StepAsync(MediaRequest request, PufferfinSettings settings, Func<IRequestWorkflow, RequestContext, Task<MediaRequest>> step)
    {
        if (_workflows.FirstOrDefault(w => w.Handles(request.Kind)) is not { } workflow)
        {
            return;
        }

        var preferred = _userSettings.Get(request.UserId).PreferredAudioLanguages is { Count: > 0 } own ? own : settings.PreferredAudioLanguages;
        var context = new RequestContext(settings, _timeProvider.GetUtcNow(), preferred);
        MediaRequest next;
        try
        {
            next = await step(workflow, context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // One request failing unexpectedly must not stop the others.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError("Request {RequestId} failed unexpectedly ({ErrorType})", request.Id, ex.GetType().Name);
            return;
        }

        if (!ReferenceEquals(next, request))
        {
            if (_store.Update(request.Id, request.Version, _ => next) is null && _store.Get(request.Id) is { } current && current.Version != request.Version)
            {
                _logger.LogInformation("Request {RequestId} changed while it was processed; the newer state wins", request.Id);
            }
        }
    }
}
