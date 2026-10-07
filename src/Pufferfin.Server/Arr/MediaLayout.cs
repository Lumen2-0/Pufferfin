using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Arr.Models;

namespace Pufferfin.Server.Arr;

/// <summary>
/// Where Pufferfin files titles, as Radarr, Sonarr and qBittorrent see the disk, and how a title is classified.
/// </summary>
public static class MediaLayout
{
    /// <summary>
    /// The qBittorrent category of Radarr downloads.
    /// </summary>
    public const string RadarrCategory = "radarr";

    /// <summary>
    /// The qBittorrent category of Sonarr downloads.
    /// </summary>
    public const string SonarrCategory = "sonarr";

    /// <summary>
    /// The qBittorrent category of the audiobooks Shelfmark downloads (its <c>QBITTORRENT_CATEGORY_AUDIOBOOK</c>).
    /// </summary>
    public const string AudiobooksCategory = "audiobooks";

    /// <summary>
    /// The qBittorrent category of the ebooks Shelfmark downloads: its <c>QBITTORRENT_CATEGORY</c>, whose default is <c>books</c>.
    /// Kept apart from <see cref="AudiobooksCategory"/>, so the two never mix.
    /// </summary>
    public const string BooksCategory = "books";

    private const int AnimationGenreId = 16;
    private const int DocumentaryGenreId = 99;

    /// <summary>
    /// Gets the Radarr root folders, by category.
    /// </summary>
    public static IReadOnlyDictionary<MediaCategory, string> RadarrRootFolders { get; } = new Dictionary<MediaCategory, string>
    {
        [MediaCategory.Standard] = "/media/movies",
        [MediaCategory.Anime] = "/media/anime-movies",
        [MediaCategory.Documentary] = "/media/documentary-movies",
    };

    /// <summary>
    /// Gets the Sonarr root folders, by category.
    /// </summary>
    public static IReadOnlyDictionary<MediaCategory, string> SonarrRootFolders { get; } = new Dictionary<MediaCategory, string>
    {
        [MediaCategory.Standard] = "/media/series",
        [MediaCategory.Anime] = "/media/anime-series",
        [MediaCategory.Documentary] = "/media/documentary-series",
    };

    /// <summary>
    /// Classifies a title from its TMDB genres, countries and original language.
    /// A documentary wins over an anime; an anime is an animation from Japan or in Japanese.
    /// </summary>
    /// <param name="genreIds">The TMDB genre ids.</param>
    /// <param name="countryCodes">The ISO 3166-1 countries of origin.</param>
    /// <param name="originalLanguage">The ISO 639-1 original language.</param>
    /// <returns>The category.</returns>
    public static MediaCategory Classify(IEnumerable<int> genreIds, IEnumerable<string> countryCodes, string? originalLanguage)
    {
        var genres = genreIds.ToHashSet();
        if (genres.Contains(DocumentaryGenreId))
        {
            return MediaCategory.Documentary;
        }

        var japanese = countryCodes.Contains("JP", StringComparer.OrdinalIgnoreCase)
            || string.Equals(originalLanguage, "ja", StringComparison.OrdinalIgnoreCase);
        return genres.Contains(AnimationGenreId) && japanese ? MediaCategory.Anime : MediaCategory.Standard;
    }

    /// <summary>
    /// Picks the root folder of a category among the folders of a service: the Pufferfin folder of the category,
    /// else a folder whose path names the category, else, for a standard title, the first folder that names no other category.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <param name="defaults">The Pufferfin folders of the service.</param>
    /// <param name="folders">The root folders of the service.</param>
    /// <returns>The path, or <c>null</c> when the service has no suitable folder.</returns>
    public static string? PickRootFolder(MediaCategory category, IReadOnlyDictionary<MediaCategory, string> defaults, IReadOnlyList<ArrRootFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(folders);
        var paths = folders.Select(f => f.Path).OfType<string>().ToList();
        var exact = paths.FirstOrDefault(p => SamePath(p, defaults[category]));
        if (exact is not null)
        {
            return exact;
        }

        var named = paths.FirstOrDefault(p => NamedCategory(p) == category);
        if (named is not null || category != MediaCategory.Standard)
        {
            // An anime or a documentary without its own folder goes with the standard titles.
            return named ?? PickRootFolder(MediaCategory.Standard, defaults, folders);
        }

        return paths.FirstOrDefault(p => NamedCategory(p) is null);
    }

    /// <summary>
    /// Gets a value indicating whether a category has a folder of its own: the Pufferfin folder, or a folder whose path names the category
    /// (for a standard title, any folder that names no other category).
    /// </summary>
    /// <param name="category">The category.</param>
    /// <param name="defaults">The Pufferfin folders of the service.</param>
    /// <param name="folders">The root folders of the service.</param>
    /// <returns><c>true</c> when the category has its folder.</returns>
    public static bool HasOwnFolder(MediaCategory category, IReadOnlyDictionary<MediaCategory, string> defaults, IReadOnlyList<ArrRootFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(folders);
        return folders
            .Select(f => f.Path)
            .OfType<string>()
            .Any(p => SamePath(p, defaults[category]) || (category == MediaCategory.Standard ? NamedCategory(p) is null : NamedCategory(p) == category));
    }

    private static MediaCategory? NamedCategory(string path)
    {
        if (path.Contains("anime", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCategory.Anime;
        }

        if (path.Contains("docu", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCategory.Documentary;
        }

        return null;
    }

    private static bool SamePath(string left, string right)
        => string.Equals(left.TrimEnd('/', '\\'), right.TrimEnd('/', '\\'), StringComparison.Ordinal);
}
