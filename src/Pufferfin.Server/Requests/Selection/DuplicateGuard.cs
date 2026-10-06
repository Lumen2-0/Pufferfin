using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Arr.Models;

namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// Prevents double downloads by checking the queue <b>and</b> the grab history of Radarr or Sonarr.
/// </summary>
/// <remarks>
/// The queue fills a while after a grab: checking it alone lets a duplicate through, the most common bug of this
/// kind of integration. The history records the grab at once. A grab covers the item until the history shows it
/// failed or was ignored, or that the file it produced was deleted afterwards.
/// </remarks>
public static class DuplicateGuard
{
    private static readonly HashSet<string> _deadStates = new(StringComparer.OrdinalIgnoreCase) { "failed", "failedPending" };
    private static readonly HashSet<string> _undoingEvents = new(StringComparer.OrdinalIgnoreCase) { "downloadFailed", "downloadIgnored" };
    private static readonly HashSet<string> _deletedEvents = new(StringComparer.OrdinalIgnoreCase) { "movieFileDeleted", "episodeFileDeleted" };

    /// <summary>
    /// Checks one item.
    /// </summary>
    /// <param name="hasFile">Whether the item has a file on disk.</param>
    /// <param name="queue">The queue items of the item.</param>
    /// <param name="history">The history records of the item, in any order.</param>
    /// <returns>The coverage.</returns>
    public static Coverage Check(bool hasFile, IEnumerable<ArrQueueItem> queue, IEnumerable<ArrHistoryRecord> history)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(history);

        var inQueue = queue.Where(q => q.TrackedDownloadState is null || !_deadStates.Contains(q.TrackedDownloadState)).ToList();
        if (inQueue.Count > 0)
        {
            return new Coverage(true, "InQueue", inQueue.Select(q => q.DownloadId).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        if (hasFile)
        {
            return new Coverage(true, "OnDisk", []);
        }

        var records = history.OrderByDescending(h => h.Date).ToList();
        var grab = records.FirstOrDefault(h => string.Equals(h.EventType, "grabbed", StringComparison.OrdinalIgnoreCase));
        if (grab is null)
        {
            return Coverage.None;
        }

        var later = records.Where(h => h.Date >= grab.Date && !ReferenceEquals(h, grab)).ToList();
        var undone = later.Any(h => h.EventType is not null && _undoingEvents.Contains(h.EventType)
            && (h.DownloadId is null || grab.DownloadId is null || string.Equals(h.DownloadId, grab.DownloadId, StringComparison.OrdinalIgnoreCase)));
        var deleted = later.Any(h => h.EventType is not null && _deletedEvents.Contains(h.EventType));
        if (undone || deleted)
        {
            return Coverage.None;
        }

        return new Coverage(true, "GrabbedInHistory", grab.DownloadId is null ? [] : [grab.DownloadId]);
    }
}
