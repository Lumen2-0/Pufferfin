using System.Collections.Generic;

namespace Pufferfin.Server.Storage;

/// <summary>
/// The seeding policy in force, with its defaults applied.
/// </summary>
/// <param name="MinDays">The minimum seeding time, in days.</param>
/// <param name="TargetRatio">The target ratio, or <c>null</c>.</param>
/// <param name="MaxDays">The maximum age, in days, or <c>null</c>.</param>
/// <param name="Categories">The managed qBittorrent categories.</param>
/// <param name="WarningPercent">The disk usage warning, in percent.</param>
public sealed record SeedPolicySummary(double MinDays, decimal? TargetRatio, double? MaxDays, IReadOnlyList<string> Categories, int WarningPercent);
