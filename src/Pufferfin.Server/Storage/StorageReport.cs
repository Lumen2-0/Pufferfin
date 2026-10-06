using System;
using System.Collections.Generic;
using Pufferfin.Server.Seeding;

namespace Pufferfin.Server.Storage;

/// <summary>
/// What the storage screen shows: the real disk usage, how much of it seeding takes, and the state of each torrent.
/// Holds no tracker URL and no passkey.
/// </summary>
/// <param name="Disks">The library disk and the downloads disk.</param>
/// <param name="Hardlinks">Whether downloads can be hard linked into the library.</param>
/// <param name="Advice">What an administrator should look at. Advice, not errors.</param>
/// <param name="Content">How the downloaded content is spread between the library and seeding.</param>
/// <param name="Torrents">The torrents the seeding policy manages, and those waiting for release.</param>
/// <param name="Policy">The seeding policy in force.</param>
/// <param name="LastCycleAt">When the hourly task last completed, or <c>null</c>.</param>
/// <param name="NextCycleAt">When the hourly task runs next.</param>
/// <param name="RecentPurges">The last torrents removed, newest first.</param>
public sealed record StorageReport(
    IReadOnlyList<DiskUsage> Disks,
    HardlinkCheckResult Hardlinks,
    IReadOnlyList<StorageAdvice> Advice,
    ContentUsage Content,
    IReadOnlyList<TorrentSeedState> Torrents,
    SeedPolicySummary Policy,
    DateTimeOffset? LastCycleAt,
    DateTimeOffset NextCycleAt,
    IReadOnlyList<PurgeRecord> RecentPurges);
