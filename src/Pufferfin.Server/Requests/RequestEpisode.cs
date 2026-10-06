namespace Pufferfin.Server.Requests;

/// <summary>
/// A Sonarr episode covered by a request, in the Sonarr numbering, which is also how the files are named.
/// </summary>
/// <param name="EpisodeId">The Sonarr episode id.</param>
/// <param name="SeasonNumber">The season number.</param>
/// <param name="EpisodeNumber">The episode number.</param>
public sealed record RequestEpisode(int EpisodeId, int SeasonNumber, int EpisodeNumber);
