using System;
using System.Collections.Concurrent;
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
/// <para>
/// A search runs beside the loop: a season of an anime can keep Sonarr searching for over ten minutes, during which
/// the downloads are still followed and the other requests still move. One search at a time per request.
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
    private readonly ConcurrentDictionary<Guid, Task> _searches = new();

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
    /// Runs one round, and waits for the searches it starts.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public Task RunRoundAsync(CancellationToken cancellationToken) => RunRoundAsync(cancellationToken, waitForSearches: true);

    /// <summary>
    /// Runs one round.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="waitForSearches">Whether to wait for the searches the round starts; the loop does not, they run beside it.</param>
    /// <returns>A task.</returns>
    private async Task RunRoundAsync(CancellationToken cancellationToken, bool waitForSearches)
    {
        var settings = _configuration.Current.Settings;
        var max = settings.MaxConcurrentDownloads ?? DefaultMaxConcurrentDownloads;

        // Follow what is under way first: a finished download frees its slot for this round.
        foreach (var request in _store.GetAll().Where(IsFollowed))
        {
            await StepAsync(request, settings, (workflow, context) => workflow.TrackAsync(request, context, cancellationToken)).ConfigureAwait(false);
        }

        // Hand the free slots out in queue order; requests waiting for sources go after the queue.
        // A request no workflow handles (a book to read, from before they were removed) stays as it is and takes no slot.
        var now = _timeProvider.GetUtcNow();
        var all = _store.GetAll().Where(HasWorkflow).ToList();
        var free = max - all.Count(r => r.TakesSlot);
        var next = all.Where(r => r.State == RequestState.Queued)
            .Concat(all.Where(r => r.State == RequestState.WaitingForSources && r.NextAttemptAt <= now).OrderBy(r => r.NextAttemptAt))
            .Take(Math.Max(0, free));
        foreach (var request in next)
        {
            _store.Update(request.Id, request.Version, r => r.To(RequestState.Searching, now, r.State == RequestState.WaitingForSources ? r.Problem : null));
        }

        var started = new List<Task>();
        foreach (var request in _store.GetAll().Where(r => r.State == RequestState.Searching && (r.NextAttemptAt is null || r.NextAttemptAt <= now) && HasWorkflow(r)))
        {
            if (_searches.ContainsKey(request.Id))
            {
                continue;
            }

            var search = new TaskCompletionSource();
            if (!_searches.TryAdd(request.Id, search.Task))
            {
                continue;
            }

            started.Add(SearchAsync(request, settings, search, cancellationToken));
        }

        if (waitForSearches)
        {
            await Task.WhenAll(started).ConfigureAwait(false);
        }
    }

    private async Task SearchAsync(MediaRequest request, PufferfinSettings settings, TaskCompletionSource search, CancellationToken cancellationToken)
    {
        try
        {
            // Off the loop: the round goes on while Radarr or Sonarr searches
            await Task.Run(() => StepAsync(request, settings, (workflow, context) => workflow.SearchAsync(request, context, cancellationToken)), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The server stops
        }
        finally
        {
            _searches.TryRemove(request.Id, out _);
            search.TrySetResult();

            // The outcome of the search is followed right away, not at the next round
            Wake();
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
                await RunRoundAsync(stoppingToken, waitForSearches: false).ConfigureAwait(false);
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

    private bool HasWorkflow(MediaRequest request) => _workflows.Any(w => w.Handles(request.Kind));

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
