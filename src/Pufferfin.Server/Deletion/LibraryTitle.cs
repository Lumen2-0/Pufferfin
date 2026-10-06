using System;
using System.Collections.Generic;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// A title of the library, as the deletion sees it.
/// </summary>
/// <param name="ItemId">The Jellyfin item id.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Title">The title, for display.</param>
/// <param name="TmdbId">The TMDB id of the film or of the series.</param>
/// <param name="TvdbId">The TVDB id of the series.</param>
/// <param name="SeasonNumber">The season number of a season or an episode.</param>
/// <param name="EpisodeNumber">The episode number of an episode.</param>
/// <param name="FilePaths">The files of an audiobook, as this server sees them.</param>
public sealed record LibraryTitle(
    Guid ItemId,
    LibraryTitleKind Kind,
    string Title,
    int? TmdbId,
    int? TvdbId,
    int? SeasonNumber,
    int? EpisodeNumber,
    IReadOnlyList<string> FilePaths);
