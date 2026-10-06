using System;
using System.Collections.Generic;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// What one cycle of the seeding policy did.
/// </summary>
/// <param name="Status">Whether the cycle ran.</param>
/// <param name="Message">An English sentence.</param>
/// <param name="Purged">The torrents removed.</param>
/// <param name="Protected">The number of torrents kept because their minimum seeding time is not reached.</param>
/// <param name="WaitingForImport">The number of torrents due for a purge but kept because their import is not confirmed.</param>
/// <param name="At">When the cycle started.</param>
public sealed record SeedCycleResult(SeedCycleStatus Status, string Message, IReadOnlyList<PurgeRecord> Purged, int Protected, int WaitingForImport, DateTimeOffset At);
