using System;

namespace Pufferfin.Server.Storage;

/// <summary>
/// The seeding state of one torrent, for the storage screen.
/// </summary>
/// <param name="Hash">The torrent hash.</param>
/// <param name="Title">The title of the library it belongs to, or the torrent name.</param>
/// <param name="Category">The qBittorrent category.</param>
/// <param name="SizeBytes">The size.</param>
/// <param name="Ratio">The share ratio.</param>
/// <param name="SeedingTime">The time it seeded.</param>
/// <param name="IsPrivate">Whether it is private; <c>null</c> when qBittorrent does not say, which counts as private.</param>
/// <param name="State">Where it stands.</param>
/// <param name="ProtectedUntil">When it reaches its minimum, if it keeps seeding.</param>
/// <param name="SharesSpaceWithLibrary">Whether its files are hard linked into the library; <c>null</c> when unknown.</param>
public sealed record TorrentSeedState(
    string Hash,
    string Title,
    string? Category,
    long SizeBytes,
    double Ratio,
    TimeSpan SeedingTime,
    bool? IsPrivate,
    SeedState State,
    DateTimeOffset? ProtectedUntil,
    bool? SharesSpaceWithLibrary);
