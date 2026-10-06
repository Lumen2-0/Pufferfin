using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Seeding;

namespace Pufferfin.Server.Storage;

/// <summary>
/// Builds what the storage screen shows: real disk usage, the hard link advice, and the seeding state of each torrent.
/// </summary>
public sealed class StorageService
{
    private const int MaxParallelFileReads = 4;

    private readonly IPufferfinConfigurationStore _configuration;
    private readonly QBittorrentClient _qBittorrent;
    private readonly ArrClient _arr;
    private readonly SeedingStore _seeding;
    private readonly SeedPolicyRunner _runner;
    private readonly HardlinkCheck _hardlinks;
    private readonly IFileLinks _links;
    private readonly IDiskSpace _diskSpace;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StorageService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageService"/> class.
    /// </summary>
    /// <param name="configuration">The configuration store.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="seeding">The seeding store.</param>
    /// <param name="runner">The seeding policy runner, for its schedule.</param>
    /// <param name="hardlinks">The hard link check.</param>
    /// <param name="links">The inodes and hard links.</param>
    /// <param name="diskSpace">The disk space reader.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public StorageService(
        IPufferfinConfigurationStore configuration,
        QBittorrentClient qBittorrent,
        ArrClient arr,
        SeedingStore seeding,
        SeedPolicyRunner runner,
        HardlinkCheck hardlinks,
        IFileLinks links,
        IDiskSpace diskSpace,
        TimeProvider timeProvider,
        ILogger<StorageService> logger)
    {
        _configuration = configuration;
        _qBittorrent = qBittorrent;
        _arr = arr;
        _seeding = seeding;
        _runner = runner;
        _hardlinks = hardlinks;
        _links = links;
        _diskSpace = diskSpace;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Builds the report.
    /// </summary>
    /// <param name="refreshHardlinks">Whether to check the hard links again rather than use the last result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    public async Task<StorageReport> GetReportAsync(bool refreshHardlinks, CancellationToken cancellationToken)
    {
        var settings = _configuration.Current.Settings;
        var paths = StoragePaths.From(settings);
        var policy = SeedPolicy.From(settings);
        var now = _timeProvider.GetUtcNow();
        var advice = new List<StorageAdvice>();

        var disks = new[] { Disk(StorageRole.Media, paths.Media, paths.WarningPercent, advice), Disk(StorageRole.Downloads, paths.Downloads, paths.WarningPercent, advice) };

        var hardlinks = _hardlinks.Check(paths, refreshHardlinks);
        if (hardlinks.Status == HardlinkStatus.Impossible)
        {
            advice.Add(new StorageAdvice(StorageAdviceCode.HardlinksImpossible, hardlinks.Message));
        }
        else if (hardlinks.Status == HardlinkStatus.Unknown)
        {
            advice.Add(new StorageAdvice(StorageAdviceCode.HardlinksUnknown, hardlinks.Message));
        }

        var torrents = new List<TorrentSeedState>();
        var content = new ContentUsage(0, 0, 0, 0);
        if (settings.GetService(PufferfinService.QBittorrent) is { } qBittorrent)
        {
            try
            {
                (torrents, content) = await TorrentsAsync(qBittorrent, settings, paths, policy, now, cancellationToken).ConfigureAwait(false);
            }
            catch (ArrApiException ex)
            {
                advice.Add(new StorageAdvice(StorageAdviceCode.QBittorrentUnavailable, ex.Message));
            }
        }

        return new StorageReport(
            disks,
            hardlinks,
            advice,
            content,
            torrents,
            new SeedPolicySummary(
                policy.MinSeedingTime.TotalDays,
                policy.TargetRatio,
                policy.MaxAge?.TotalDays,
                policy.Categories.Order(StringComparer.Ordinal).ToList(),
                paths.WarningPercent),
            _seeding.LastCycleCompletedAt,
            _runner.NextCycleDueAt() is var due && due < now ? now : due,
            _seeding.GetRecentPurges().Take(20).ToList());
    }

    private static SeedState State(SeedEvaluation evaluation, bool released) => evaluation.Verdict switch
    {
        _ when evaluation.IsPurge => SeedState.DueForPurge,
        SeedVerdict.Protected or SeedVerdict.Downloading when released => SeedState.WaitingForRelease,
        SeedVerdict.Downloading => SeedState.Downloading,
        SeedVerdict.Protected => SeedState.Protected,
        _ => SeedState.Seeding
    };

    private static string EpisodeTitle(ArrHistoryRecord record)
        => record.Episode is { } episode
            ? string.Create(CultureInfo.InvariantCulture, $"{record.Series?.Title} S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}")
            : record.Series?.Title ?? string.Empty;

    private DiskUsage Disk(StorageRole role, string path, int warningPercent, List<StorageAdvice> advice)
    {
        if (_diskSpace.Read(path) is not { Total: > 0 } space)
        {
            advice.Add(new StorageAdvice(StorageAdviceCode.DirectoryMissing, $"{path} does not exist on this server, or its disk cannot be read."));
            return new DiskUsage(role, path, false, 0, 0, 0, false);
        }

        var (total, free) = space;
        var percent = Math.Round((total - free) * 100.0 / total, 1);
        var above = percent >= warningPercent;
        if (above)
        {
            advice.Add(new StorageAdvice(
                StorageAdviceCode.DiskAlmostFull,
                string.Create(CultureInfo.InvariantCulture, $"The disk of {path} is {percent}% full (warning at {warningPercent}%).")));
        }

        return new DiskUsage(role, path, true, total, free, percent, above);
    }

    private async Task<(List<TorrentSeedState> Torrents, ContentUsage Content)> TorrentsAsync(
        ServiceSettings qBittorrent,
        PufferfinSettings settings,
        StoragePaths paths,
        SeedPolicy policy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = _seeding.GetPendingReleases().ToDictionary(p => p.Hash, StringComparer.OrdinalIgnoreCase);
        var torrents = (await _qBittorrent.GetAllTorrentsAsync(qBittorrent, cancellationToken).ConfigureAwait(false))
            .Where(t => !string.IsNullOrEmpty(t.Hash) && (policy.Manages(t.Category) || pending.ContainsKey(t.Hash!)))
            .ToList();
        var titles = await TitlesAsync(settings, torrents, cancellationToken).ConfigureAwait(false);

        var files = new ConcurrentDictionary<string, (long Shared, long SeedingOnly, long Unknown)>(StringComparer.OrdinalIgnoreCase);
        if (_links.IsSupported)
        {
            await Parallel.ForEachAsync(
                torrents.Where(t => SeedEvaluator.CompletedAt(t) is not null && !string.IsNullOrEmpty(t.SavePath)),
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelFileReads, CancellationToken = cancellationToken },
                async (torrent, token) => files[torrent.Hash!] = await SpaceAsync(qBittorrent, paths, torrent, token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        var states = new List<TorrentSeedState>();
        long shared = 0, seedingOnly = 0, unknown = 0;
        foreach (var torrent in torrents)
        {
            var hash = torrent.Hash!.ToLowerInvariant();
            var released = pending.TryGetValue(hash, out var release);
            var evaluation = SeedEvaluator.Evaluate(torrent, policy, now, released);
            bool? sharesSpace = null;
            if (files.TryGetValue(hash, out var space))
            {
                shared += space.Shared;
                seedingOnly += space.SeedingOnly;
                unknown += space.Unknown;
                sharesSpace = space.Unknown > 0 ? null : space.SeedingOnly == 0;
            }
            else
            {
                unknown += torrent.Size;
            }

            states.Add(new TorrentSeedState(
                hash,
                release?.Title ?? titles.GetValueOrDefault(hash) ?? torrent.Name ?? hash,
                torrent.Category,
                torrent.Size,
                Math.Round(torrent.Ratio, 3),
                evaluation.SeedingTime,
                torrent.IsPrivate ?? release?.IsPrivate,
                State(evaluation, released),
                evaluation.ProtectedUntil,
                sharesSpace));
        }

        return (states, new ContentUsage(torrents.Sum(t => t.Size), shared, seedingOnly, unknown));
    }

    private async Task<(long Shared, long SeedingOnly, long Unknown)> SpaceAsync(ServiceSettings qBittorrent, StoragePaths paths, QBittorrentTorrent torrent, CancellationToken cancellationToken)
    {
        IReadOnlyList<QBittorrentFile> files;
        try
        {
            files = await _qBittorrent.GetFilesAsync(qBittorrent, torrent.Hash!, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException)
        {
            return (0, 0, torrent.Size);
        }

        long shared = 0, seedingOnly = 0, unknown = 0;
        foreach (var file in files.Where(f => f.Priority > 0 && !string.IsNullOrEmpty(f.Name)))
        {
            switch (_links.GetIdentity(paths.ToLocalPath(Path.Join(torrent.SavePath, file.Name))))
            {
                case null:
                    unknown += file.Size;
                    break;
                case { LinkCount: > 1 }:
                    shared += file.Size;
                    break;
                default:
                    seedingOnly += file.Size;
                    break;
            }
        }

        return (shared, seedingOnly, unknown);
    }

    private async Task<Dictionary<string, string>> TitlesAsync(PufferfinSettings settings, IReadOnlyList<QBittorrentTorrent> torrents, CancellationToken cancellationToken)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (torrents.Count == 0)
        {
            return titles;
        }

        var since = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, torrents.Min(t => t.AddedOn))) - TimeSpan.FromDays(1);
        foreach (var service in new[] { PufferfinService.Radarr, PufferfinService.Sonarr })
        {
            if (settings.GetService(service) is not { } arr)
            {
                continue;
            }

            try
            {
                foreach (var record in await _arr.GetHistorySinceAsync(arr, since, cancellationToken).ConfigureAwait(false))
                {
                    if (string.IsNullOrEmpty(record.DownloadId) || titles.ContainsKey(record.DownloadId))
                    {
                        continue;
                    }

                    var title = service == PufferfinService.Radarr ? record.Movie?.Title : EpisodeTitle(record);
                    if (!string.IsNullOrEmpty(title))
                    {
                        titles[record.DownloadId] = title;
                    }
                }
            }
            catch (ArrApiException ex)
            {
                // The torrent names stand in for the titles.
                _logger.LogDebug("The history of {Service} is unavailable for the storage screen: {Failure}", service, ex.Message);
            }
        }

        return titles;
    }
}
