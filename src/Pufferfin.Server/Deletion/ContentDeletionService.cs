using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Integration;
using Pufferfin.Server.Seeding;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// Deletes a title of the library, safely by default: the library files go now, and a private torrent keeps seeding
/// until its minimum seeding time before the hourly task purges it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is changed before the plan is complete: a service that cannot be reached stops the deletion before it
/// starts. The torrents kept are recorded as waiting for release before the files are deleted, so a restart in
/// between cannot lose them. A torrent of unknown privacy is treated as private.
/// </para>
/// <para>
/// The immediate mode removes everything now, private torrents included. It needs an administrator and an explicit
/// confirmation; without it, nothing is deleted and the plan comes back with its warning.
/// </para>
/// </remarks>
public sealed class ContentDeletionService
{
    private readonly ILibraryContent _library;
    private readonly IMediaLibrary _mediaLibrary;
    private readonly TitleTorrentResolver _resolver;
    private readonly ArrClient _arr;
    private readonly QBittorrentClient _qBittorrent;
    private readonly SeedingStore _seeding;
    private readonly RequestStore _requests;
    private readonly IPufferfinConfigurationStore _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ContentDeletionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDeletionService"/> class.
    /// </summary>
    /// <param name="library">The library content.</param>
    /// <param name="mediaLibrary">The library, to scan after a deletion.</param>
    /// <param name="resolver">Links titles to torrents.</param>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="seeding">The seeding store.</param>
    /// <param name="requests">The request store, which holds the permissions.</param>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public ContentDeletionService(
        ILibraryContent library,
        IMediaLibrary mediaLibrary,
        TitleTorrentResolver resolver,
        ArrClient arr,
        QBittorrentClient qBittorrent,
        SeedingStore seeding,
        RequestStore requests,
        IPufferfinConfigurationStore configuration,
        TimeProvider timeProvider,
        ILogger<ContentDeletionService> logger)
    {
        _library = library;
        _mediaLibrary = mediaLibrary;
        _resolver = resolver;
        _arr = arr;
        _qBittorrent = qBittorrent;
        _seeding = seeding;
        _requests = requests;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Tells what deleting a title would do, without deleting anything.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="userId">The user.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome, <see cref="DeletionStatus.Planned"/> with the plan when the title can be deleted.</returns>
    public async Task<DeletionOutcome> PreviewAsync(Guid itemId, DeletionMode mode, Guid userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var (outcome, _) = await PlanAsync(itemId, mode, userId, isAdministrator, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// Deletes a title.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="mode">The mode; <see cref="DeletionMode.Safe"/> unless the caller asks otherwise.</param>
    /// <param name="confirmed">Whether the user confirmed the immediate mode after reading its warning.</param>
    /// <param name="userId">The user.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<DeletionOutcome> DeleteAsync(Guid itemId, DeletionMode mode, bool confirmed, Guid userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var (outcome, resolved) = await PlanAsync(itemId, mode, userId, isAdministrator, cancellationToken).ConfigureAwait(false);
        if (outcome.Status != DeletionStatus.Planned || outcome.Plan is not { } plan || resolved is null)
        {
            return outcome;
        }

        if (mode == DeletionMode.Immediate && !confirmed)
        {
            return outcome with { Status = DeletionStatus.ConfirmationRequired, Message = plan.Warning ?? "Confirm the immediate deletion." };
        }

        var now = _timeProvider.GetUtcNow();
        var settings = _configuration.Current.Settings;

        // Recorded first: whatever happens next, a kept torrent stays protected and is released later.
        var alreadyPending = _seeding.GetPendingReleases().Select(p => p.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keep = plan.Torrents.Where(t => t.Action == TorrentAction.KeepUntilMinimum).ToList();
        _seeding.AddPendingReleases(keep.Select(t => new PendingRelease(t.Hash, plan.Title.Title, t.IsPrivate, now, userId)));

        try
        {
            await DeleteFilesAsync(resolved, settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArrApiException or IOException or UnauthorizedAccessException)
        {
            _seeding.RemovePendingReleases(keep.Select(t => t.Hash).Where(h => !alreadyPending.Contains(h)));
            _logger.LogWarning("Deleting {Title} failed, nothing was deleted: {Failure}", plan.Title.Title, ex.Message);
            return new DeletionOutcome(DeletionStatus.ServiceUnavailable, ex is ArrApiException ? ex.Message : "The files could not be deleted.", plan);
        }

        var message = await RemoveTorrentsAsync(plan, settings, now, userId, cancellationToken).ConfigureAwait(false);
        if (plan.Title.Kind != LibraryTitleKind.Audiobook)
        {
            _mediaLibrary.Refresh(null);
        }

        _logger.LogInformation(
            "{Title} deleted ({Mode}): {Removed} torrents removed, {Kept} kept until their minimum",
            plan.Title.Title,
            mode,
            plan.Torrents.Count(t => t.Action == TorrentAction.RemoveNow),
            keep.Count);
        return new DeletionOutcome(DeletionStatus.Deleted, message, plan);
    }

    private static string Days(TimeSpan time) => time.TotalDays.ToString("0.#", CultureInfo.InvariantCulture);

    private static TorrentPlan Plan(QBittorrentTorrent torrent, bool? isPrivate, SeedPolicy policy, DeletionMode mode, DateTimeOffset now)
    {
        var evaluation = SeedEvaluator.Evaluate(torrent, policy, now, released: true);
        var beforeMinimum = evaluation.Verdict is SeedVerdict.Protected or SeedVerdict.Downloading;
        var keep = mode == DeletionMode.Safe && isPrivate != false && beforeMinimum;
        return new TorrentPlan(
            torrent.Hash!.ToLowerInvariant(),
            torrent.Name ?? torrent.Hash!,
            isPrivate,
            evaluation.SeedingTime,
            keep ? evaluation.ProtectedUntil : null,
            keep ? TorrentAction.KeepUntilMinimum : TorrentAction.RemoveNow);
    }

    private async Task<(DeletionOutcome Outcome, ResolvedTitle? Resolved)> PlanAsync(Guid itemId, DeletionMode mode, Guid userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        if (!isAdministrator && !_requests.GetPermissions(userId).CanDelete)
        {
            return (new DeletionOutcome(DeletionStatus.Forbidden, "This account may not delete titles."), null);
        }

        if (mode == DeletionMode.Immediate && !isAdministrator)
        {
            return (new DeletionOutcome(DeletionStatus.Forbidden, "Only an administrator may delete immediately."), null);
        }

        if (_library.Find(itemId) is not { } title)
        {
            return (new DeletionOutcome(DeletionStatus.NotFound, "No such film, series, season, episode or audiobook in the library."), null);
        }

        var settings = _configuration.Current.Settings;
        var service = title.Kind switch
        {
            LibraryTitleKind.Movie => PufferfinService.Radarr,
            LibraryTitleKind.Audiobook => (PufferfinService?)null,
            _ => PufferfinService.Sonarr
        };
        if (service is PufferfinService needed && !settings.IsEnabled(needed))
        {
            return (new DeletionOutcome(DeletionStatus.ServiceDisabled, $"{needed} is not configured, so Pufferfin cannot delete this title."), null);
        }

        ResolvedTitle resolved;
        try
        {
            resolved = await _resolver.ResolveAsync(title, settings, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            return (new DeletionOutcome(DeletionStatus.ServiceUnavailable, ex.Message), null);
        }

        var policy = SeedPolicy.From(settings);
        var now = _timeProvider.GetUtcNow();
        var torrents = new List<TorrentPlan>();
        if (settings.GetService(PufferfinService.QBittorrent) is { } qBittorrent && resolved.Releasable.Count + resolved.Shared.Count > 0)
        {
            IReadOnlyList<QBittorrentTorrent> found;
            try
            {
                found = await _qBittorrent.GetTorrentsAsync(qBittorrent, resolved.Releasable.Concat(resolved.Shared).ToList(), cancellationToken).ConfigureAwait(false);
            }
            catch (ArrApiException ex)
            {
                // Without qBittorrent nothing says which torrents are private: nothing is deleted.
                return (new DeletionOutcome(DeletionStatus.ServiceUnavailable, ex.Message), null);
            }

            var shared = resolved.Shared.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var wanted = resolved.Releasable.Concat(resolved.Shared).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var torrent in found.Where(t => !string.IsNullOrEmpty(t.Hash) && wanted.Contains(t.Hash)))
            {
                if (shared.Contains(torrent.Hash!))
                {
                    torrents.Add(new TorrentPlan(torrent.Hash!.ToLowerInvariant(), torrent.Name ?? torrent.Hash!, torrent.IsPrivate, TimeSpan.Zero, null, TorrentAction.KeepShared));
                    continue;
                }

                var isPrivate = torrent.IsPrivate ?? await IsPrivateAsync(qBittorrent, torrent.Hash!, cancellationToken).ConfigureAwait(false);
                torrents.Add(Plan(torrent, isPrivate, policy, mode, now));
            }
        }

        var beforeMinimum = torrents.Count(t => t.Action != TorrentAction.KeepShared
            && t.IsPrivate != false
            && (t.Action == TorrentAction.KeepUntilMinimum || t.SeedingTime < policy.MinSeedingTime));
        string? warning = null;
        if (mode == DeletionMode.Immediate)
        {
            warning = beforeMinimum > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{beforeMinimum} private torrents have not seeded their minimum of {Days(policy.MinSeedingTime)} days. Deleting them now can get the tracker account banned for hit and run. The safe deletion keeps them seeding until then.")
                : "Every file of this title is deleted now, torrents included.";
        }

        var plan = new DeletionPlan(title, mode, torrents, beforeMinimum, warning);
        return (new DeletionOutcome(DeletionStatus.Planned, "Ready to delete.", plan), resolved);
    }

    private async Task<bool?> IsPrivateAsync(ServiceSettings qBittorrent, string hash, CancellationToken cancellationToken)
    {
        try
        {
            return await _qBittorrent.GetIsPrivateAsync(qBittorrent, hash, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException)
        {
            return null;
        }
    }

    private async Task DeleteFilesAsync(ResolvedTitle resolved, PufferfinSettings settings, CancellationToken cancellationToken)
    {
        var title = resolved.Title;
        if (resolved.ArrId is not int arrId)
        {
            // Not held by Radarr or Sonarr (an audiobook, or a title added by hand): the server deletes the files.
            _library.DeleteFiles(title.ItemId);
            return;
        }

        switch (title.Kind)
        {
            case LibraryTitleKind.Movie:
                await _arr.DeleteMovieAsync(settings.GetService(PufferfinService.Radarr)!, arrId, cancellationToken).ConfigureAwait(false);
                break;
            case LibraryTitleKind.Series:
                await _arr.DeleteSeriesAsync(settings.GetService(PufferfinService.Sonarr)!, arrId, cancellationToken).ConfigureAwait(false);
                break;
            default:
                var sonarr = settings.GetService(PufferfinService.Sonarr)!;

                // Unmonitored first, so Sonarr does not download again what is being deleted.
                if (resolved.EpisodeIds.Count > 0)
                {
                    await _arr.UnmonitorEpisodesAsync(sonarr, resolved.EpisodeIds, cancellationToken).ConfigureAwait(false);
                }

                foreach (var fileId in resolved.EpisodeFileIds)
                {
                    await _arr.DeleteEpisodeFileAsync(sonarr, fileId, cancellationToken).ConfigureAwait(false);
                }

                break;
        }
    }

    private async Task<string> RemoveTorrentsAsync(DeletionPlan plan, PufferfinSettings settings, DateTimeOffset now, Guid userId, CancellationToken cancellationToken)
    {
        var remove = plan.Torrents.Where(t => t.Action == TorrentAction.RemoveNow).ToList();
        var kept = plan.Torrents.Count(t => t.Action == TorrentAction.KeepUntilMinimum);
        var keptMessage = kept == 0 ? string.Empty : $" {kept} private torrents keep seeding until their minimum, then are purged.";
        if (remove.Count == 0 || settings.GetService(PufferfinService.QBittorrent) is not { } qBittorrent)
        {
            return "Deleted." + keptMessage;
        }

        var reason = plan.Mode == DeletionMode.Immediate ? PurgeReason.ImmediateDeletion : PurgeReason.SafeDeletion;
        _seeding.BeginPurges(remove.Select(t => new PurgeRecord(t.Hash, plan.Title.Title, now, reason)));
        try
        {
            await _qBittorrent.DeleteTorrentsAsync(qBittorrent, remove.Select(t => t.Hash).ToList(), deleteFiles: true, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            // The title is gone from the library: its torrents are released by the hourly task instead.
            _seeding.AddPendingReleases(remove.Select(t => new PendingRelease(t.Hash, plan.Title.Title, t.IsPrivate, now, userId)));
            _logger.LogWarning("The torrents of {Title} could not be removed now; the hourly task will: {Failure}", plan.Title.Title, ex.Message);
            return "Deleted. qBittorrent could not be reached: its torrents are removed by the hourly task." + keptMessage;
        }

        _seeding.CompletePurges(remove.Select(t => t.Hash));
        return "Deleted." + keptMessage;
    }
}
