namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// What a release must cover.
/// </summary>
/// <param name="Mode">The kind of release wanted.</param>
/// <param name="SeasonNumber">The Sonarr season number, for a season or an episode.</param>
/// <param name="EpisodeNumber">The Sonarr episode number, for an episode.</param>
public sealed record SelectionScope(SelectionMode Mode, int? SeasonNumber = null, int? EpisodeNumber = null)
{
    /// <summary>
    /// Gets the scope of a film.
    /// </summary>
    public static SelectionScope Movie { get; } = new(SelectionMode.Movie);
}
