using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Storage;

/// <summary>
/// Checks that a file of the downloads directory can be hard linked into the library directory, so seeding takes no
/// extra space. Two directories on different disks, or on different mounts of a container, cannot share files.
/// </summary>
/// <remarks>
/// The check creates an empty probe file in the downloads directory, links it into the library directory, and
/// deletes both. Comparing devices is not enough: two bind mounts of the same disk report the same device, yet the
/// kernel refuses a link between them. The result is kept an hour.
/// </remarks>
public sealed class HardlinkCheck
{
    /// <summary>
    /// How long a result is kept.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private const string ProbePrefix = ".pufferfin-hardlink-probe-";

    private readonly IFileLinks _links;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HardlinkCheck> _logger;
    private readonly ConcurrentDictionary<(string Media, string Downloads), (HardlinkCheckResult Result, DateTimeOffset At)> _cache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HardlinkCheck"/> class.
    /// </summary>
    /// <param name="links">The inodes and hard links.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public HardlinkCheck(IFileLinks links, TimeProvider timeProvider, ILogger<HardlinkCheck> logger)
    {
        _links = links;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether hard links work from the downloads directory to the library directory.
    /// </summary>
    /// <param name="paths">The storage paths.</param>
    /// <param name="refresh">Whether to check again even when a recent result is kept.</param>
    /// <returns>The result.</returns>
    public HardlinkCheckResult Check(StoragePaths paths, bool refresh = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var key = (paths.Media, paths.Downloads);
        var now = _timeProvider.GetUtcNow();
        if (!refresh && _cache.TryGetValue(key, out var cached) && now - cached.At < CacheDuration)
        {
            return cached.Result;
        }

        var result = Probe(paths);
        _cache[key] = (result, now);
        if (result.Status == HardlinkStatus.Impossible)
        {
            _logger.LogWarning("Hard links from {DownloadsPath} to {MediaPath} are impossible: seeding takes space twice", paths.Downloads, paths.Media);
        }

        return result;
    }

    private HardlinkCheckResult Probe(StoragePaths paths)
    {
        if (!_links.IsSupported)
        {
            return new HardlinkCheckResult(HardlinkStatus.Unknown, "This platform does not let Pufferfin check hard links.");
        }

        if (!Directory.Exists(paths.Media) || !Directory.Exists(paths.Downloads))
        {
            return new HardlinkCheckResult(HardlinkStatus.Unknown, $"{paths.Media} or {paths.Downloads} does not exist on this server. Check MEDIA_PATH and DOWNLOADS_PATH.");
        }

        var name = ProbePrefix + Guid.NewGuid().ToString("N");
        var source = Path.Join(paths.Downloads, name);
        var target = Path.Join(paths.Media, name);
        try
        {
            try
            {
                File.WriteAllBytes(source, []);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new HardlinkCheckResult(HardlinkStatus.Unknown, $"Pufferfin cannot write to {paths.Downloads}, so it cannot check hard links.");
            }

            return _links.TryCreateHardLink(source, target)
                ? new HardlinkCheckResult(HardlinkStatus.Possible, $"{paths.Downloads} and {paths.Media} share their files: seeding takes no extra space.")
                : new HardlinkCheckResult(
                    HardlinkStatus.Impossible,
                    $"{paths.Downloads} and {paths.Media} are not on the same disk or mount: hard links are impossible, so every seeded download takes its space twice. Put both under one disk and one mount.");
        }
        finally
        {
            TryDelete(target);
            TryDelete(source);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("The hard link probe {ProbePath} could not be deleted", path);
        }
    }
}
