using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// Deletes the titles whose scheduled deletion is due, through the safe deletion: the library files go, a public
/// torrent goes with them, and a private torrent keeps seeding until its minimum before the hourly task removes it.
/// </summary>
/// <remarks>
/// Only the title scheduled is ever deleted, after a last check that the item found under its id is still that title
/// (same kind, same TMDB and TVDB ids). Anything else stops before a change and the deletion says it needs an
/// administrator. A run is idempotent: the outcome is recorded, and a deletion already handled is never looked at again.
/// </remarks>
public sealed class ScheduledDeletionRunner : IDisposable
{
    private readonly ScheduledDeletionStore _store;
    private readonly ContentDeletionService _deletion;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduledDeletionRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledDeletionRunner"/> class.
    /// </summary>
    /// <param name="store">The scheduled deletions.</param>
    /// <param name="deletion">The deletion service.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public ScheduledDeletionRunner(ScheduledDeletionStore store, ContentDeletionService deletion, TimeProvider timeProvider, ILogger<ScheduledDeletionRunner> logger)
    {
        _store = store;
        _deletion = deletion;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Deletes every title whose time is up.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deletions handled; the others are tried again on the next run.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var handled = 0;
            foreach (var deletion in _store.GetAll().Where(d => ScheduledDeletions.Decide(d, now) == ScheduledDeletionDecision.Delete))
            {
                if (await DeleteAsync(deletion, now, cancellationToken).ConfigureAwait(false))
                {
                    handled++;
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

    private async Task<bool> DeleteAsync(ScheduledDeletion deletion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outcome = await _deletion.DeleteCheckedAsync(
            deletion.ItemId,
            deletion.UserId,
            (plan, _) => ScheduledDeletions.Mismatch(deletion, plan.Title),
            cancellationToken).ConfigureAwait(false);

        switch (outcome.Status)
        {
            case DeletionStatus.Deleted:
                var kept = outcome.Plan?.Torrents.Count(t => t.Action == TorrentAction.KeepUntilMinimum) ?? 0;
                _logger.LogInformation(
                    "Scheduled deletion of {Title} done; {Kept} private torrents keep seeding until their minimum, then are removed with their files",
                    deletion.Title,
                    kept);
                _store.Record(deletion.ItemId, ScheduledDeletionOutcome.Removed, now);
                return true;
            case DeletionStatus.NotFound:
                _logger.LogInformation("Scheduled deletion of {Title}: it was no longer in the library, nothing deleted", deletion.Title);
                _store.Record(deletion.ItemId, ScheduledDeletionOutcome.AlreadyGone, now);
                return true;
            case DeletionStatus.ServiceUnavailable:
            case DeletionStatus.ServiceDisabled:
                // Nothing was deleted: tried again on the next run.
                _logger.LogWarning("Scheduled deletion of {Title} could not run yet: {Reason}", deletion.Title, outcome.Message);
                return false;
            default:
                _logger.LogWarning("Scheduled deletion of {Title} was not done ({Status}: {Reason}); an administrator decides", deletion.Title, outcome.Status, outcome.Message);
                _store.Record(deletion.ItemId, ScheduledDeletionOutcome.NeedsAttention, now, outcome.Message);
                return true;
        }
    }
}
