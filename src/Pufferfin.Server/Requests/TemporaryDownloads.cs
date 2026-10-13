using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Arr.Models;

namespace Pufferfin.Server.Requests;

/// <summary>
/// The rules of the temporary films: how long they stay, which download is theirs, and when they leave. Pure, so the
/// expiry can run again at any time with the same result.
/// </summary>
public static class TemporaryDownloads
{
    /// <summary>
    /// How long a temporary film stays in the library after its import.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets when a request that becomes available now expires: only a temporary film whose own download is known
    /// expires. A film that was already there when it was asked for is never removed by it.
    /// </summary>
    /// <param name="request">The request, about to become available.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The expiry, or <c>null</c> when the film stays.</returns>
    public static DateTimeOffset? ExpiryFor(MediaRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Temporary && request.Kind == RequestKind.Movie && !string.IsNullOrEmpty(request.TorrentHash) ? now + Lifetime : null;
    }

    /// <summary>
    /// Decides what the expiry does with a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="all">Every request, to find another one that wants the same film to stay.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The decision.</returns>
    public static TemporaryDecision Decide(MediaRequest request, IEnumerable<MediaRequest> all, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(all);
        if (!request.Temporary
            || request.Kind != RequestKind.Movie
            || request.State != RequestState.Available
            || request.ExpiresAt is not { } expiresAt
            || request.ExpiryOutcome is not null
            || string.IsNullOrEmpty(request.TorrentHash))
        {
            return TemporaryDecision.None;
        }

        if (now < expiresAt)
        {
            return TemporaryDecision.NotDue;
        }

        // Another request for the same film, not temporary, still on its way or done: that user wants it to stay.
        var wanted = all.Any(other => !other.Id.Equals(request.Id)
            && other.Kind == RequestKind.Movie
            && other.TmdbId == request.TmdbId
            && !other.Temporary
            && (other.IsActive || other.State == RequestState.Available));
        return wanted ? TemporaryDecision.Keep : TemporaryDecision.Remove;
    }

    /// <summary>
    /// Finds the download of a temporary film: the torrent Radarr is downloading for it, else the last one it grabbed
    /// for it since the request was made. Only a download that started after the request counts as its own.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="queue">The Radarr queue of the film.</param>
    /// <param name="history">The Radarr history of the film, or <c>null</c> when not read.</param>
    /// <returns>The torrent hash, lower case, or <c>null</c>.</returns>
    public static string? FindDownload(MediaRequest request, IReadOnlyList<ArrQueueItem> queue, IReadOnlyList<ArrHistoryRecord>? history)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(queue);
        var hashes = queue.Select(q => q.DownloadId).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hashes.Count == 1)
        {
            return hashes[0]!.ToLowerInvariant();
        }

        if (hashes.Count > 1)
        {
            // Two downloads at once for one film: which one is the request's is not certain.
            return null;
        }

        return history?
            .Where(h => string.Equals(h.EventType, "grabbed", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(h.DownloadId)
                && h.Date >= request.CreatedAt)
            .OrderByDescending(h => h.Date)
            .Select(h => h.DownloadId!.ToLowerInvariant())
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks a deletion plan against what the request downloaded, before anything is deleted: the same film in the
    /// library and in Radarr, and no torrent but its own.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="tmdbId">The TMDB id of the title found in the library.</param>
    /// <param name="arrId">The Radarr id of the title, if Radarr has it.</param>
    /// <param name="torrents">The hashes of the torrents the deletion would release (shared ones excluded).</param>
    /// <returns>Why the deletion must not happen, or <c>null</c> when it matches.</returns>
    public static string? Mismatch(MediaRequest request, int? tmdbId, int? arrId, IEnumerable<string> torrents)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(torrents);
        if (tmdbId != request.TmdbId)
        {
            return "the film in the library is not the one requested";
        }

        if (request.ArrId is int expected && arrId is int actual && expected != actual)
        {
            return "Radarr holds the film under another id";
        }

        var foreign = torrents.Where(hash => !string.Equals(hash, request.TorrentHash, StringComparison.OrdinalIgnoreCase)).ToList();
        return foreign.Count > 0 ? $"{foreign.Count} other torrents hold the film" : null;
    }
}
