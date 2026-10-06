using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// The state of the seeding policy, persisted in a JSON file of the server configuration directory: the torrents
/// waiting for their release, the purges under way, the recent purges and when the last hourly cycle ran.
/// </summary>
/// <remarks>
/// A purge is journaled before qBittorrent is asked, and closed after: a cycle cut by a restart is finished by the
/// next one, and a torrent already removed is never counted twice. Every write replaces the file atomically.
/// </remarks>
public sealed class SeedingStore
{
    /// <summary>
    /// The number of purges kept for display.
    /// </summary>
    public const int RecentPurgeCount = 200;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger<SeedingStore> _logger;
    private readonly Lock _lock = new();
    private Document _document;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeedingStore"/> class.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="logger">The logger.</param>
    public SeedingStore(string path, ILogger<SeedingStore> logger)
    {
        _path = path;
        _logger = logger;
        _document = Read();
    }

    /// <summary>
    /// Gets when the last hourly cycle started, or <c>null</c> when none ever ran.
    /// </summary>
    public DateTimeOffset? LastCycleStartedAt
    {
        get
        {
            lock (_lock)
            {
                return _document.LastCycleStartedAt;
            }
        }
    }

    /// <summary>
    /// Gets when the last hourly cycle completed, or <c>null</c> when none ever completed.
    /// </summary>
    public DateTimeOffset? LastCycleCompletedAt
    {
        get
        {
            lock (_lock)
            {
                return _document.LastCycleCompletedAt;
            }
        }
    }

    /// <summary>
    /// Gets the torrents waiting for their release.
    /// </summary>
    /// <returns>The pending releases.</returns>
    public IReadOnlyList<PendingRelease> GetPendingReleases()
    {
        lock (_lock)
        {
            return _document.PendingReleases.ToList();
        }
    }

    /// <summary>
    /// Gets the purges asked of qBittorrent and not confirmed yet.
    /// </summary>
    /// <returns>The purges, by hash.</returns>
    public IReadOnlyList<PurgeRecord> GetPurgesInFlight()
    {
        lock (_lock)
        {
            return _document.PurgesInFlight.ToList();
        }
    }

    /// <summary>
    /// Gets the recent purges, newest first.
    /// </summary>
    /// <returns>The purges.</returns>
    public IReadOnlyList<PurgeRecord> GetRecentPurges()
    {
        lock (_lock)
        {
            return _document.RecentPurges.OrderByDescending(p => p.At).ToList();
        }
    }

    /// <summary>
    /// Records torrents waiting for their release. A torrent already waiting keeps its first record.
    /// </summary>
    /// <param name="releases">The releases.</param>
    public void AddPendingReleases(IEnumerable<PendingRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        lock (_lock)
        {
            var changed = false;
            foreach (var release in releases)
            {
                if (!_document.PendingReleases.Any(p => string.Equals(p.Hash, release.Hash, StringComparison.OrdinalIgnoreCase)))
                {
                    _document.PendingReleases.Add(release with { Hash = release.Hash.ToLowerInvariant() });
                    changed = true;
                }
            }

            if (changed)
            {
                Write();
            }
        }
    }

    /// <summary>
    /// Forgets torrents waiting for their release, for example because they left qBittorrent.
    /// </summary>
    /// <param name="hashes">The torrent hashes.</param>
    public void RemovePendingReleases(IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        var set = hashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            if (_document.PendingReleases.RemoveAll(p => set.Contains(p.Hash)) > 0)
            {
                Write();
            }
        }
    }

    /// <summary>
    /// Records the start of an hourly cycle, before it touches anything.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void BeginCycle(DateTimeOffset now)
    {
        lock (_lock)
        {
            _document.LastCycleStartedAt = now;
            Write();
        }
    }

    /// <summary>
    /// Records the end of an hourly cycle.
    /// </summary>
    /// <param name="now">The current time.</param>
    public void CompleteCycle(DateTimeOffset now)
    {
        lock (_lock)
        {
            _document.LastCycleCompletedAt = now;
            Write();
        }
    }

    /// <summary>
    /// Journals purges before qBittorrent is asked to remove the torrents.
    /// </summary>
    /// <param name="purges">The purges.</param>
    public void BeginPurges(IEnumerable<PurgeRecord> purges)
    {
        ArgumentNullException.ThrowIfNull(purges);
        lock (_lock)
        {
            foreach (var purge in purges)
            {
                _document.PurgesInFlight.RemoveAll(p => string.Equals(p.Hash, purge.Hash, StringComparison.OrdinalIgnoreCase));
                _document.PurgesInFlight.Add(purge with { Hash = purge.Hash.ToLowerInvariant() });
            }

            Write();
        }
    }

    /// <summary>
    /// Closes purges qBittorrent confirmed: they leave the journal and the pending releases, and join the recent purges once.
    /// </summary>
    /// <param name="hashes">The torrent hashes.</param>
    public void CompletePurges(IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        var set = hashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            var done = _document.PurgesInFlight.Where(p => set.Contains(p.Hash)).ToList();
            _document.PurgesInFlight.RemoveAll(p => set.Contains(p.Hash));
            _document.PendingReleases.RemoveAll(p => set.Contains(p.Hash));
            foreach (var purge in done)
            {
                if (!_document.RecentPurges.Any(p => string.Equals(p.Hash, purge.Hash, StringComparison.OrdinalIgnoreCase)))
                {
                    _document.RecentPurges.Add(purge);
                }
            }

            if (_document.RecentPurges.Count > RecentPurgeCount)
            {
                _document.RecentPurges = _document.RecentPurges.OrderByDescending(p => p.At).Take(RecentPurgeCount).ToList();
            }

            Write();
        }
    }

    private Document Read()
    {
        if (!File.Exists(_path))
        {
            return new Document();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<Document>(stream, _jsonOptions) ?? new Document();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // The policy still guarantees the minimum to every torrent: losing the file never purges early.
            var backup = _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(_path, backup);
            _logger.LogError("The Pufferfin seeding file could not be read ({ErrorType}). It was moved to {BackupPath} and starts empty", ex.GetType().Name, backup);
            return new Document();
        }
    }

    private void Write()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _path + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, _document, _jsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed class Document
    {
        public int Version { get; set; } = 1;

        public DateTimeOffset? LastCycleStartedAt { get; set; }

        public DateTimeOffset? LastCycleCompletedAt { get; set; }

        public List<PendingRelease> PendingReleases { get; set; } = [];

        public List<PurgeRecord> PurgesInFlight { get; set; } = [];

        public List<PurgeRecord> RecentPurges { get; set; } = [];
    }
}
