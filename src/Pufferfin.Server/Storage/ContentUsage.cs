namespace Pufferfin.Server.Storage;

/// <summary>
/// How the downloaded content is spread between the library and seeding.
/// </summary>
/// <param name="TorrentBytes">The size of every torrent reported.</param>
/// <param name="SharedWithLibraryBytes">The part hard linked into the library: seeding it takes no extra space.</param>
/// <param name="SeedingOnlyBytes">The part only seeding holds: what purging the torrents would free.</param>
/// <param name="UnknownBytes">The part whose files could not be read.</param>
public sealed record ContentUsage(long TorrentBytes, long SharedWithLibraryBytes, long SeedingOnlyBytes, long UnknownBytes);
