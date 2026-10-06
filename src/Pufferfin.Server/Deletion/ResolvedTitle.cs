using System.Collections.Generic;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// A title to delete, with what holds its files and the torrents it came from.
/// </summary>
/// <param name="Title">The title.</param>
/// <param name="ArrId">The Radarr film id or the Sonarr series id, or <c>null</c> when the service does not have it.</param>
/// <param name="EpisodeIds">The Sonarr episodes of a season or an episode.</param>
/// <param name="EpisodeFileIds">The Sonarr episode files of a season or an episode.</param>
/// <param name="Releasable">The torrents only this title uses: they follow the deletion mode.</param>
/// <param name="Shared">The torrents other titles of the library still use: they stay under the seeding policy.</param>
public sealed record ResolvedTitle(
    LibraryTitle Title,
    int? ArrId,
    IReadOnlyList<int> EpisodeIds,
    IReadOnlyList<int> EpisodeFileIds,
    IReadOnlyList<string> Releasable,
    IReadOnlyList<string> Shared);
