using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.AutoConfig.Steps;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests.Selection;

namespace Pufferfin.Server.Requests.Workflow;

/// <summary>
/// What the Radarr and Sonarr workflows share: failure handling, indexer diagnosis, quality profiles and progress.
/// </summary>
public abstract class ArrWorkflowBase : IRequestWorkflow
{
    /// <summary>
    /// The number of searches after which a request whose downloads keep failing stops.
    /// </summary>
    public const int MaxSearchAttempts = 3;

    /// <summary>
    /// How long a grab may stay invisible in the queue and the history before the request searches again.
    /// </summary>
    public static readonly TimeSpan GrabGracePeriod = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a request waits after a service could not be reached.
    /// </summary>
    public static readonly TimeSpan UnreachableRetryDelay = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> _importStates = new(StringComparer.OrdinalIgnoreCase) { "importPending", "importing", "imported" };

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrWorkflowBase"/> class.
    /// </summary>
    /// <param name="service">Radarr or Sonarr.</param>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="logger">The logger.</param>
    protected ArrWorkflowBase(PufferfinService service, ArrClient arr, QBittorrentClient qBittorrent, ILogger logger)
    {
        Service = service;
        Arr = arr;
        QBittorrent = qBittorrent;
        Logger = logger;
    }

    /// <summary>Gets the service: Radarr or Sonarr.</summary>
    protected PufferfinService Service { get; }

    /// <summary>Gets the Radarr and Sonarr client.</summary>
    protected ArrClient Arr { get; }

    /// <summary>Gets the qBittorrent client.</summary>
    protected QBittorrentClient QBittorrent { get; }

    /// <summary>Gets the logger.</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public abstract bool Handles(RequestKind kind);

    /// <inheritdoc />
    public async Task<MediaRequest> SearchAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Settings.GetService(Service) is not { } service)
        {
            return request.Fail(context.Now, RequestProblemCode.ServiceDisabled, $"{Service} is not configured.");
        }

        try
        {
            return await SearchCoreAsync(request, service, context, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            return OnServiceFailure(request, ex, context);
        }
    }

    /// <inheritdoc />
    public async Task<MediaRequest> TrackAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // A service turned off meanwhile: nothing to follow, the request keeps its state.
        if (context.Settings.GetService(Service) is not { } service || request.ArrId is null)
        {
            return request;
        }

        try
        {
            return await TrackCoreAsync(request, service, context, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            return OnServiceFailure(request, ex, context);
        }
    }

    /// <summary>
    /// Searches, once the service is known to be configured.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="service">The service settings.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next version of the request.</returns>
    protected abstract Task<MediaRequest> SearchCoreAsync(MediaRequest request, ServiceSettings service, RequestContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Tracks, once the service is known to be configured and the title added to it.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="service">The service settings.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next version of the request.</returns>
    protected abstract Task<MediaRequest> TrackCoreAsync(MediaRequest request, ServiceSettings service, RequestContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Turns a search without a selected release into a wait or a failure, telling apart no release yet,
    /// unreachable indexers and no indexer at all.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="service">The service settings.</param>
    /// <param name="problem">The problem of the selection.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The waiting or failed request.</returns>
    protected async Task<MediaRequest> NoReleaseAsync(MediaRequest request, ServiceSettings service, RequestProblem problem, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(context);
        var searched = request with { Attempts = request.Attempts + 1 };
        if (problem.Code != RequestProblemCode.NoRelease)
        {
            return searched.To(RequestState.Failed, context.Now, problem);
        }

        var indexers = (await Arr.GetIndexersAsync(service, cancellationToken).ConfigureAwait(false))
            .Where(i => i["enableInteractiveSearch"]?.GetValue<bool>() != false)
            .Select(i => i["id"]?.GetValue<int>())
            .OfType<int>()
            .ToHashSet();
        if (indexers.Count == 0)
        {
            return searched.Fail(context.Now, RequestProblemCode.NoIndexer, $"{Service} has no indexer able to search. Check Prowlarr.");
        }

        var failing = (await Arr.GetIndexerStatusesAsync(service, cancellationToken).ConfigureAwait(false))
            .Where(s => s["disabledTill"] is JsonValue till && DateTimeOffset.TryParse(till.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until) && until > context.Now)
            .Select(s => s["indexerId"]?.GetValue<int>())
            .OfType<int>()
            .ToHashSet();
        if (indexers.IsSubsetOf(failing))
        {
            return searched.Wait(context.Now, new RequestProblem(
                RequestProblemCode.IndexersUnavailable,
                $"The indexers of {Service} cannot be reached for now. Pufferfin will search again."));
        }

        return searched.Wait(context.Now, problem with { Message = "No release was found yet. Pufferfin will search again, and the title stays monitored." });
    }

    /// <summary>
    /// Gets the id of the Pufferfin quality profile of a language, falling back to the first profile of the service.
    /// </summary>
    /// <param name="service">The service settings.</param>
    /// <param name="candidate">The language.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profile id, or <c>null</c> when the service has no profile.</returns>
    protected async Task<int?> ProfileIdAsync(ServiceSettings service, LanguageCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var profiles = await Arr.GetItemsAsync(service, "qualityprofile", cancellationToken).ConfigureAwait(false);
        var name = ProfileName(candidate);
        return (profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? profiles.FirstOrDefault())?.Id;
    }

    /// <summary>
    /// Gets the profile to switch a title to after taking a release in a language: only when the title has a Pufferfin
    /// language profile, so a profile chosen by hand is kept.
    /// </summary>
    /// <param name="service">The service settings.</param>
    /// <param name="currentProfileId">The current profile of the title.</param>
    /// <param name="candidate">The language of the release taken.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profile id to switch to, or <c>null</c> to keep the current one.</returns>
    protected async Task<int?> AlignedProfileIdAsync(ServiceSettings service, int currentProfileId, LanguageCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var profiles = await Arr.GetItemsAsync(service, "qualityprofile", cancellationToken).ConfigureAwait(false);
        var current = profiles.FirstOrDefault(p => p.Id == currentProfileId);
        var target = profiles.FirstOrDefault(p => string.Equals(p.Name, ProfileName(candidate), StringComparison.OrdinalIgnoreCase));
        var isPufferfin = current?.Name?.StartsWith(LanguageProfilesStep.ProfileName(string.Empty), StringComparison.OrdinalIgnoreCase) == true;
        return isPufferfin && target is not null && target.Id != currentProfileId ? target.Id : null;
    }

    /// <summary>
    /// Follows the queue items of a request: progress from qBittorrent when it knows the torrents, else from the queue,
    /// and the problems the service reports.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="queue">The queue items of the request.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next version of the request.</returns>
    protected async Task<MediaRequest> FollowQueueAsync(MediaRequest request, IReadOnlyList<ArrQueueItem> queue, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(context);

        var blocked = queue.FirstOrDefault(q => string.Equals(q.TrackedDownloadState, "importBlocked", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(q.TrackedDownloadState, "importPending", StringComparison.OrdinalIgnoreCase) && string.Equals(q.TrackedDownloadStatus, "warning", StringComparison.OrdinalIgnoreCase)));
        if (blocked is not null)
        {
            return request.Fail(context.Now, RequestProblemCode.ImportBlocked, $"{Service} cannot import the download.", Messages(blocked));
        }

        if (queue.All(q => q.TrackedDownloadState is not null && _importStates.Contains(q.TrackedDownloadState)))
        {
            return request.To(RequestState.Importing, context.Now);
        }

        var warning = queue.FirstOrDefault(q => string.Equals(q.TrackedDownloadStatus, "warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(q.TrackedDownloadStatus, "error", StringComparison.OrdinalIgnoreCase));
        var problem = warning is null ? null : new RequestProblem(RequestProblemCode.DownloadWarning, "The download client reports a problem. The download goes on.", Messages(warning));
        var downloading = request.To(RequestState.Downloading, context.Now, problem);
        var progress = await ProgressAsync(queue, context.Settings, cancellationToken).ConfigureAwait(false);
        return progress == downloading.Progress ? downloading : downloading with { Progress = progress };
    }

    /// <summary>
    /// Gets the reason of the last failed download of a request, from the history.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="history">The history records of the request items.</param>
    /// <returns>The reason, or <c>null</c> when no download failed since the grab.</returns>
    protected static string? LastFailure(MediaRequest request, IEnumerable<ArrHistoryRecord> history)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failure = history
            .Where(h => string.Equals(h.EventType, "downloadFailed", StringComparison.OrdinalIgnoreCase) && (request.GrabbedAt is null || h.Date >= request.GrabbedAt))
            .MaxBy(h => h.Date);
        if (failure is null)
        {
            return null;
        }

        return failure.Data is not null && failure.Data.TryGetValue("message", out var message) && !string.IsNullOrWhiteSpace(message)
            ? message
            : failure.SourceTitle ?? "The download failed.";
    }

    /// <summary>
    /// Decides what follows when nothing is in flight anymore and the files are not there: wait for a grab that is not
    /// visible yet, search again, or stop when downloads keep failing.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="failure">The reason of the last failed download, if any.</param>
    /// <param name="context">The context.</param>
    /// <returns>The next version of the request.</returns>
    protected MediaRequest NothingInFlight(MediaRequest request, string? failure, RequestContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (request.State == RequestState.WaitingForSources)
        {
            return request;
        }

        if (failure is null && request.GrabbedAt is { } grabbedAt && context.Now - grabbedAt < GrabGracePeriod)
        {
            return request;
        }

        if (request.Attempts >= MaxSearchAttempts)
        {
            return failure is null
                ? request.Wait(context.Now, new RequestProblem(RequestProblemCode.NoRelease, "No download got through yet. Pufferfin will search again."))
                : request.Fail(context.Now, RequestProblemCode.DownloadFailed, $"The download failed {request.Attempts} times.", failure);
        }

        // The service blocklists a failed release: the next search takes another one.
        return request.To(RequestState.Searching, context.Now, failure is null ? null : new RequestProblem(RequestProblemCode.DownloadFailed, "The download failed. Pufferfin searches for another release.", failure));
    }

    /// <summary>
    /// Gets the name of the Pufferfin quality profile of a language.
    /// </summary>
    /// <param name="candidate">The language.</param>
    /// <returns>The profile name.</returns>
    protected static string ProfileName(LanguageCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.IsOriginal ? LanguageProfilesStep.OriginalProfileName : LanguageProfilesStep.ProfileName(ArrLanguages.GetName(candidate.Code) ?? candidate.Code);
    }

    private static string? Messages(ArrQueueItem item)
    {
        var messages = item.StatusMessages
            .SelectMany(m => m.Messages.Count > 0 ? m.Messages : [m.Title ?? string.Empty])
            .Append(item.ErrorMessage ?? string.Empty)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return messages.Count == 0 ? null : string.Join(" ", messages);
    }

    private MediaRequest OnServiceFailure(MediaRequest request, ArrApiException ex, RequestContext context)
    {
        Logger.LogWarning("Request {RequestId}: {Failure}", request.Id, ex.Message);
        var detail = ex.RemoteMessages.Count == 0 ? null : string.Join(" ", ex.RemoteMessages);
        if (ex.Failure is ArrFailure.Rejected or ArrFailure.NotFound && request.State == RequestState.Searching)
        {
            return request.Fail(context.Now, RequestProblemCode.ServiceUnavailable, ex.Message, detail);
        }

        // Unreachable, slow or refusing the key: transient, or for an administrator to fix. The request keeps its state.
        var problem = new RequestProblem(RequestProblemCode.ServiceUnavailable, ex.Message, detail);
        var kept = request.Problem == problem ? request : request with { Problem = problem, UpdatedAt = context.Now };
        return request.State == RequestState.Searching ? kept with { NextAttemptAt = context.Now + UnreachableRetryDelay } : kept;
    }

    private async Task<RequestProgress?> ProgressAsync(IReadOnlyList<ArrQueueItem> queue, PufferfinSettings settings, CancellationToken cancellationToken)
    {
        var size = queue.Sum(q => q.Size);
        var hashes = queue.Select(q => q.DownloadId).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (settings.GetService(PufferfinService.QBittorrent) is { } qBittorrent && hashes.Count > 0)
        {
            try
            {
                var torrents = await QBittorrent.GetTorrentsAsync(qBittorrent, hashes, cancellationToken).ConfigureAwait(false);
                if (torrents.Count > 0)
                {
                    var fraction = size > 0
                        ? queue.Sum(q => q.Size * (torrents.FirstOrDefault(t => string.Equals(t.Hash, q.DownloadId, StringComparison.OrdinalIgnoreCase))?.Progress ?? 0)) / size
                        : torrents.Average(t => t.Progress);
                    var etas = torrents.Where(t => t.Eta is > 0 and < 8640000).Select(t => t.Eta).ToList();
                    return new RequestProgress(Math.Round(fraction, 4), torrents.Sum(t => t.DownloadSpeed), etas.Count == 0 ? null : etas.Max());
                }
            }
            catch (ArrApiException ex)
            {
                // The queue of the service still gives the progress.
                Logger.LogDebug("qBittorrent progress unavailable: {Failure}", ex.Message);
            }
        }

        if (size <= 0)
        {
            return null;
        }

        var left = queue.Select(q => TimeSpan.TryParse(q.Timeleft, CultureInfo.InvariantCulture, out var timeLeft) ? (long?)timeLeft.TotalSeconds : null).OfType<long>().ToList();
        return new RequestProgress(Math.Round((size - queue.Sum(q => q.Sizeleft)) / size, 4), null, left.Count == 0 ? null : left.Max());
    }
}
