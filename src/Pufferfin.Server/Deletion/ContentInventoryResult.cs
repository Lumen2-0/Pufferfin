using System.Collections.Generic;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// The content manager list.
/// </summary>
/// <param name="Rows">Every whole title on disk.</param>
/// <param name="TorrentsAvailable">Whether the torrents could be read; without them the rows have none.</param>
/// <param name="MinSeedingDays">The minimum seeding time of private torrents, in days.</param>
public sealed record ContentInventoryResult(IReadOnlyList<ContentRow> Rows, bool TorrentsAvailable, double MinSeedingDays);
