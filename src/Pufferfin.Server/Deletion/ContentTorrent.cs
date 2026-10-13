using System;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// A torrent of a title of the content manager.
/// </summary>
/// <param name="Hash">The torrent hash.</param>
/// <param name="Name">The torrent name.</param>
/// <param name="AddedAt">When qBittorrent added it.</param>
/// <param name="Ratio">The share ratio.</param>
/// <param name="IsPrivate">Whether its tracker is private; <c>null</c> when unknown, treated as private.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="ProtectedUntil">Until when it must keep seeding, for a private torrent before its minimum.</param>
public sealed record ContentTorrent(
    string Hash,
    string Name,
    DateTimeOffset? AddedAt,
    double Ratio,
    bool? IsPrivate,
    long SizeBytes,
    DateTimeOffset? ProtectedUntil);
