using System;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// The verdict of the seeding policy on a torrent.
/// </summary>
/// <param name="Verdict">The verdict.</param>
/// <param name="SeedingTime">The time the torrent seeded so far.</param>
/// <param name="ProtectedUntil">When a protected torrent reaches its minimum, if it keeps seeding.</param>
public sealed record SeedEvaluation(SeedVerdict Verdict, TimeSpan SeedingTime, DateTimeOffset? ProtectedUntil)
{
    /// <summary>
    /// Gets a value indicating whether the torrent is purged.
    /// </summary>
    public bool IsPurge => Verdict is SeedVerdict.PurgeTargetRatio or SeedVerdict.PurgeMaxAge or SeedVerdict.PurgeReleased;
}
