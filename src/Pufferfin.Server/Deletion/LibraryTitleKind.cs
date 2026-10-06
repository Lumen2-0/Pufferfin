namespace Pufferfin.Server.Deletion;

/// <summary>
/// What a deleted title is, which decides who deletes its files and how its torrents are found.
/// </summary>
public enum LibraryTitleKind
{
    /// <summary>
    /// A film: Radarr deletes it; its torrents come from the Radarr history.
    /// </summary>
    Movie,

    /// <summary>
    /// A whole series: Sonarr deletes it; its torrents come from the Sonarr history.
    /// </summary>
    Series,

    /// <summary>
    /// A season: Sonarr deletes its files and stops monitoring it.
    /// </summary>
    Season,

    /// <summary>
    /// An episode: Sonarr deletes its file and stops monitoring it.
    /// </summary>
    Episode,

    /// <summary>
    /// An audiobook: the server deletes its files; its torrents are found by inode.
    /// </summary>
    Audiobook
}
