using System;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// What a deletion does to one torrent.
/// </summary>
/// <param name="Hash">The torrent hash.</param>
/// <param name="Name">The torrent name.</param>
/// <param name="IsPrivate">Whether the torrent is private; <c>null</c> when unknown, which counts as private.</param>
/// <param name="SeedingTime">The time it seeded so far.</param>
/// <param name="MinimumReachedAt">When a torrent kept until its minimum reaches it, if it keeps seeding.</param>
/// <param name="Action">What happens to it.</param>
public sealed record TorrentPlan(string Hash, string Name, bool? IsPrivate, TimeSpan SeedingTime, DateTimeOffset? MinimumReachedAt, TorrentAction Action);
