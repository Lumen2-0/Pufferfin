using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// Removes the temporary films whose time is up: the film leaves Radarr and the library, its public torrent goes with
/// its files, and a private torrent keeps seeding until its minimum seeding time before the hourly task removes it.
/// </summary>
/// <remarks>
/// <para>
/// Only the film of the request is ever deleted, through the safe deletion, and only after a last check of the
/// complete plan: the film found in the library has the TMDB id of the request, Radarr holds it under the id the
/// request added, and no torrent but the one the request downloaded would be released. Anything else stops the
/// expiry before a change, and the request says it needs an administrator.
/// </para>
/// <para>
/// A run is idempotent: the outcome is recorded on the request, and a request already handled is never looked at
/// again. A run cut short by a restart finds the film gone, or deletes it then.
/// </para>
/// </remarks>
public sealed class TemporaryExpiryRunner : IDisposable
{
    private readonly RequestStore _requests;
    private readonly ILibraryContent _library;
    private readonly ContentDeletionService _deletion;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TemporaryExpiryRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="TemporaryExpiryRunner"/> class.
    /// </summary>
    /// <param name="requests">The request store.</param>
    /// <param name="library">The library content.</param>
    /// <param name="deletion">The deletion service.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public TemporaryExpiryRunner(RequestStore requests, ILibraryContent library, ContentDeletionService deletion, TimeProvider timeProvider, ILogger<TemporaryExpiryRunner> logger)
    {
        _requests = requests;
        _library = library;
        _deletion = deletion;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Handles every temporary film whose time is up.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of requests handled; the others are tried again on the next run.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var all = _requests.GetAll();
            var handled = 0;
            foreach (var request in all)
            {
                switch (TemporaryDownloads.Decide(request, all, now))
                {
                    case TemporaryDecision.Keep:
                        _logger.LogInformation("Temporary film {Title} stays: another request wants it", request.Title);
                        Record(request, TemporaryExpiryOutcome.Kept, now);
                        handled++;
                        break;
                    case TemporaryDecision.Remove:
                        if (await ExpireAsync(request, now, cancellationToken).ConfigureAwait(false))
                        {
                            handled++;
                        }

                        break;
                    default:
                        break;
                }
            }

            return handled;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<bool> ExpireAsync(MediaRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var items = _library.FindMovies(request.TmdbId);
        if (items.Count == 0)
        {
            _logger.LogInformation("Temporary film {Title} expired: it was no longer in the library, nothing deleted", request.Title);
            Record(request, TemporaryExpiryOutcome.AlreadyGone, now);
            return true;
        }

        if (items.Count > 1)
        {
            _logger.LogWarning("Temporary film {Title} expired, but the library holds it {Count} times: nothing deleted", request.Title, items.Count);
            Record(request, TemporaryExpiryOutcome.NeedsAttention, now);
            return true;
        }

        var outcome = await _deletion.DeleteCheckedAsync(
            items[0],
            request.UserId,
            (plan, resolved) => plan.Title.Kind != LibraryTitleKind.Movie
                ? "the library item is not a film"
                : TemporaryDownloads.Mismatch(
                    request,
                    plan.Title.TmdbId,
                    resolved.ArrId,
                    plan.Torrents.Where(t => t.Action != TorrentAction.KeepShared).Select(t => t.Hash)),
            cancellationToken).ConfigureAwait(false);

        switch (outcome.Status)
        {
            case DeletionStatus.Deleted:
                var kept = outcome.Plan?.Torrents.Count(t => t.Action == TorrentAction.KeepUntilMinimum) ?? 0;
                _logger.LogInformation(
                    "Temporary film {Title} expired and was removed; {Kept} private torrents keep seeding until their minimum, then are removed with their files",
                    request.Title,
                    kept);
                Record(request, TemporaryExpiryOutcome.Removed, now);
                return true;
            case DeletionStatus.NotFound:
                Record(request, TemporaryExpiryOutcome.AlreadyGone, now);
                return true;
            case DeletionStatus.ServiceUnavailable:
            case DeletionStatus.ServiceDisabled:
                // Nothing was deleted: tried again on the next run.
                _logger.LogWarning("Temporary film {Title} expired but could not be removed yet: {Reason}", request.Title, outcome.Message);
                return false;
            default:
                _logger.LogWarning("Temporary film {Title} expired but was not removed ({Status}: {Reason}); an administrator decides", request.Title, outcome.Status, outcome.Message);
                Record(request, TemporaryExpiryOutcome.NeedsAttention, now);
                return true;
        }
    }

    private void Record(MediaRequest request, TemporaryExpiryOutcome outcome, DateTimeOffset now)
        => _requests.Update(request.Id, null, current => current.ExpiryOutcome is null ? current with { ExpiryOutcome = outcome, ExpiredAt = now } : current);
}
