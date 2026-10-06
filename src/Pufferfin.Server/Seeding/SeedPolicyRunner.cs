using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// Runs one cycle of the seeding policy: purges the torrents past their minimum seeding time that reached the target
/// ratio or the maximum age, and the torrents whose title was deleted with the safe mode once their minimum is reached.
/// </summary>
/// <remarks>
/// <para>
/// Only torrents of the managed categories (<c>SEED_CATEGORIES</c>) and torrents waiting for their release are
/// looked at. No torrent is purged before its minimum seeding time, whatever its ratio, its age or its privacy.
/// A torrent whose import into the library is not confirmed is kept.
/// </para>
/// <para>
/// A cycle is idempotent: its verdicts only depend on the state of qBittorrent, and a purge is journaled before it
/// is asked and closed after. Cycles never overlap. The schedule is persisted: a restart neither runs a cycle twice
/// within the hour nor skips one that came due while the server was down.
/// </para>
/// </remarks>
public sealed class SeedPolicyRunner : IDisposable
{
    /// <summary>
    /// The time between two cycles.
    /// </summary>
    public static readonly TimeSpan CycleInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// The time before a cycle that did not complete is run again.
    /// </summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    private readonly IPufferfinConfigurationStore _configuration;
    private readonly QBittorrentClient _qBittorrent;
    private readonly ArrClient _arr;
    private readonly SeedingStore _store;
    private readonly IFileLinks _links;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SeedPolicyRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SeedPolicyRunner"/> class.
    /// </summary>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="store">The seeding store.</param>
    /// <param name="links">The inodes and hard links.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public SeedPolicyRunner(
        IPufferfinConfigurationStore configuration,
        QBittorrentClient qBittorrent,
        ArrClient arr,
        SeedingStore store,
        IFileLinks links,
        TimeProvider timeProvider,
        ILogger<SeedPolicyRunner> logger)
    {
        _configuration = configuration;
        _qBittorrent = qBittorrent;
        _arr = arr;
        _store = store;
        _links = links;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets when the next cycle is due: an hour after the last one started, five minutes after a cycle that did not
    /// complete, now when none ever ran.
    /// </summary>
    /// <returns>The due time.</returns>
    public DateTimeOffset NextCycleDueAt()
    {
        if (_store.LastCycleStartedAt is not { } started)
        {
            return DateTimeOffset.MinValue;
        }

        var completed = _store.LastCycleCompletedAt is { } end && end >= started;
        return started + (completed ? CycleInterval : RetryDelay);
    }

    /// <summary>
    /// Runs a cycle when one is due.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or <c>null</c> when no cycle was due.</returns>
    public async Task<SeedCycleResult?> RunCycleIfDueAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _timeProvider.GetUtcNow() >= NextCycleDueAt() ? await RunCoreAsync(cancellationToken).ConfigureAwait(false) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs a cycle now, for an administrator. Safe at any time: a cycle is idempotent.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<SeedCycleResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<SeedCycleResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        _store.BeginCycle(now);
        var settings = _configuration.Current.Settings;
        if (settings.GetService(PufferfinService.QBittorrent) is not { } qBittorrent)
        {
            _store.CompleteCycle(now);
            return new SeedCycleResult(SeedCycleStatus.Skipped, "qBittorrent is not configured.", [], 0, 0, now);
        }

        IReadOnlyList<QBittorrentTorrent> torrents;
        try
        {
            torrents = await _qBittorrent.GetAllTorrentsAsync(qBittorrent, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            _logger.LogWarning("Seeding policy: {Failure}. Nothing was purged; the cycle runs again in a few minutes", ex.Message);
            return new SeedCycleResult(SeedCycleStatus.Failed, ex.Message, [], 0, 0, now);
        }

        var byHash = torrents.Where(t => !string.IsNullOrEmpty(t.Hash)).ToDictionary(t => t.Hash!.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

        // A purge journaled by a cycle cut short: done if the torrent is gone, else decided again below.
        var gone = _store.GetPurgesInFlight().Select(p => p.Hash).Where(h => !byHash.ContainsKey(h)).ToList();
        if (gone.Count > 0)
        {
            _store.CompletePurges(gone);
        }

        var pending = _store.GetPendingReleases().ToDictionary(p => p.Hash, StringComparer.OrdinalIgnoreCase);
        var vanished = pending.Keys.Where(h => !byHash.ContainsKey(h)).ToList();
        if (vanished.Count > 0)
        {
            // Removed by hand in qBittorrent: nothing left to release.
            _store.RemovePendingReleases(vanished);
        }

        var policy = SeedPolicy.From(settings);
        var imports = new ImportConfirmation(_arr, _qBittorrent, _links, settings, _logger);
        var purges = new List<PurgeRecord>();
        var protectedCount = 0;
        var waitingForImport = 0;
        foreach (var (hash, torrent) in byHash)
        {
            var released = pending.TryGetValue(hash, out var release);
            if (!released && !policy.Manages(torrent.Category))
            {
                continue;
            }

            var evaluation = SeedEvaluator.Evaluate(torrent, policy, now, released);
            if (evaluation.Verdict == SeedVerdict.Protected)
            {
                protectedCount++;
            }

            if (!evaluation.IsPurge)
            {
                continue;
            }

            // A released title was deleted from the library on purpose: its download has nothing left to import.
            if (!released && !await imports.IsImportedAsync(torrent, cancellationToken).ConfigureAwait(false))
            {
                waitingForImport++;
                continue;
            }

            purges.Add(new PurgeRecord(hash, release?.Title ?? torrent.Name ?? hash, now, Reason(evaluation.Verdict)));
        }

        if (purges.Count > 0)
        {
            _store.BeginPurges(purges);
            try
            {
                await _qBittorrent.DeleteTorrentsAsync(qBittorrent, purges.Select(p => p.Hash).ToList(), deleteFiles: true, cancellationToken).ConfigureAwait(false);
            }
            catch (ArrApiException ex)
            {
                // The journal keeps them: the next cycle decides again.
                _logger.LogWarning("Seeding policy: qBittorrent did not remove {Count} torrents: {Failure}", purges.Count, ex.Message);
                return new SeedCycleResult(SeedCycleStatus.Failed, ex.Message, [], protectedCount, waitingForImport, now);
            }

            _store.CompletePurges(purges.Select(p => p.Hash));
            foreach (var purge in purges)
            {
                _logger.LogInformation("Seeding policy: removed the torrent of {Title} ({Reason})", purge.Title, purge.Reason);
            }
        }

        _store.CompleteCycle(now);
        return new SeedCycleResult(
            SeedCycleStatus.Completed,
            purges.Count == 0 ? "Nothing to purge." : $"{purges.Count} torrents purged.",
            purges,
            protectedCount,
            waitingForImport,
            now);
    }

    private static PurgeReason Reason(SeedVerdict verdict) => verdict switch
    {
        SeedVerdict.PurgeTargetRatio => PurgeReason.TargetRatio,
        SeedVerdict.PurgeMaxAge => PurgeReason.MaxAge,
        _ => PurgeReason.Released
    };
}
