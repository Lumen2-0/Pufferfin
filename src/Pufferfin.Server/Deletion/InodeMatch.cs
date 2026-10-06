namespace Pufferfin.Server.Deletion;

/// <summary>
/// A torrent holding files of a deleted audiobook.
/// </summary>
/// <param name="Hash">The torrent hash.</param>
/// <param name="FullyCovered">Whether every file of the torrent is deleted or unused by the library, so the torrent can be released.</param>
public sealed record InodeMatch(string Hash, bool FullyCovered);
