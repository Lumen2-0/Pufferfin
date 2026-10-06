using System;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// A torrent whose title was deleted with the safe mode but that still seeds, waiting for its minimum seeding time.
/// </summary>
/// <param name="Hash">The torrent hash, lower case.</param>
/// <param name="Title">The title, for display.</param>
/// <param name="IsPrivate">Whether the torrent is private; <c>null</c> when qBittorrent did not say, which counts as private.</param>
/// <param name="RequestedAt">When the title was deleted.</param>
/// <param name="RequestedBy">The user who deleted it.</param>
public sealed record PendingRelease(string Hash, string Title, bool? IsPrivate, DateTimeOffset RequestedAt, Guid RequestedBy);
