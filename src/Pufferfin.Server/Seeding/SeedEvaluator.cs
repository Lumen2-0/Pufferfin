using System;
using Pufferfin.Server.Arr;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// Applies the seeding policy to one torrent. Pure: the same torrent at the same time always gets the same verdict,
/// which is what makes the hourly task idempotent.
/// </summary>
public static class SeedEvaluator
{
    /// <summary>
    /// Decides what happens to a torrent.
    /// </summary>
    /// <param name="torrent">The torrent.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="now">The current time.</param>
    /// <param name="released">Whether its title was deleted with the safe mode, so it only waits for its minimum.</param>
    /// <returns>The evaluation.</returns>
    public static SeedEvaluation Evaluate(QBittorrentTorrent torrent, SeedPolicy policy, DateTimeOffset now, bool released)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        ArgumentNullException.ThrowIfNull(policy);

        var completedAt = CompletedAt(torrent);
        if (completedAt is null)
        {
            return new SeedEvaluation(SeedVerdict.Downloading, TimeSpan.Zero, null);
        }

        var seeded = SeedingTime(torrent, completedAt.Value, now);

        // The guarantee comes first: nothing below may purge a torrent that has not seeded its minimum.
        if (seeded < policy.MinSeedingTime)
        {
            return new SeedEvaluation(SeedVerdict.Protected, seeded, now + (policy.MinSeedingTime - seeded));
        }

        if (released)
        {
            return new SeedEvaluation(SeedVerdict.PurgeReleased, seeded, null);
        }

        if (policy.TargetRatio is decimal target && (decimal)torrent.Ratio >= target)
        {
            return new SeedEvaluation(SeedVerdict.PurgeTargetRatio, seeded, null);
        }

        if (policy.MaxAge is TimeSpan maxAge && now - completedAt.Value >= maxAge)
        {
            return new SeedEvaluation(SeedVerdict.PurgeMaxAge, seeded, null);
        }

        return new SeedEvaluation(SeedVerdict.Seeding, seeded, null);
    }

    /// <summary>
    /// Gets when a torrent completed, or <c>null</c> while it downloads.
    /// </summary>
    /// <param name="torrent">The torrent.</param>
    /// <returns>The completion time.</returns>
    public static DateTimeOffset? CompletedAt(QBittorrentTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        return torrent.Progress >= 1 && torrent.CompletionOn > 0 ? DateTimeOffset.FromUnixTimeSeconds(torrent.CompletionOn) : null;
    }

    /// <summary>
    /// Gets the time a torrent seeded. qBittorrent counts it, which is what trackers count too: a stopped torrent does
    /// not seed. Older versions that do not count it fall back to the time since completion.
    /// </summary>
    /// <param name="torrent">The torrent.</param>
    /// <param name="completedAt">When it completed.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The seeding time.</returns>
    private static TimeSpan SeedingTime(QBittorrentTorrent torrent, DateTimeOffset completedAt, DateTimeOffset now)
        => torrent.SeedingTime is long seconds and >= 0 ? TimeSpan.FromSeconds(seconds) : now - completedAt;
}
