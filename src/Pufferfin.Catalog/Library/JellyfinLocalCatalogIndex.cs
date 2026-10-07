using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Library;

/// <summary>
/// <see cref="ILocalCatalogIndex"/> backed by the Jellyfin library and its search providers.
/// </summary>
public sealed class JellyfinLocalCatalogIndex : ILocalCatalogIndex
{
    /// <summary>
    /// The provider id keys an Audible ASIN may be stored under by the audiobook metadata plugins.
    /// </summary>
    private static readonly string[] _asinProviderKeys = ["Audible", "Asin", "AudibleAsin"];

    private readonly ISearchManager _searchManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLocalCatalogIndex"/> class.
    /// </summary>
    /// <param name="searchManager">The Jellyfin search manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    public JellyfinLocalCatalogIndex(ISearchManager searchManager, ILibraryManager libraryManager, IUserManager userManager)
    {
        _searchManager = searchManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalCatalogEntry>> SearchAsync(string query, Guid userId, IReadOnlyCollection<CatalogItemKind> kinds, int limit, CancellationToken cancellationToken)
    {
        var itemKinds = kinds.Select(ToItemKind).ToList();
        if (itemKinds.Count == 0)
        {
            return [];
        }

        var withAudiobooks = kinds.Contains(CatalogItemKind.Audiobook);

        var result = await _searchManager.GetSearchHintsAsync(
            new SearchQuery
            {
                UserId = userId,
                SearchTerm = query,
                Limit = limit,
                IncludeItemTypes = itemKinds.ToArray(),
                IncludePeople = kinds.Contains(CatalogItemKind.Person),
                IncludeMedia = true,
                IncludeGenres = false,
                IncludeStudios = false,
                IncludeArtists = false
            },
            cancellationToken).ConfigureAwait(false);

        var entries = result.Items
            .Select(hint => withAudiobooks ? ToAudiobookEntry(hint.Item) ?? ToEntry(hint.Item) : ToEntry(hint.Item))
            .OfType<LocalCatalogEntry>();
        if (withAudiobooks)
        {
            entries = entries.Concat(SearchBookFolders(query, userId, limit));
        }

        return entries
            .DistinctBy(entry => entry.ItemId)
            .ToList();
    }

    /// <summary>
    /// Finds the books told in several files by the name of their folder: the search index leaves plain folders out,
    /// and their files carry chapter titles that never match the title of the book.
    /// </summary>
    private IEnumerable<LocalCatalogEntry> SearchBookFolders(string query, Guid userId, int limit)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return [];
        }

        var folders = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Folder],
            Recursive = true,
            SearchTerm = query,
            Limit = limit
        });
        return folders.Select(ToAudiobookEntry).OfType<LocalCatalogEntry>();
    }

    /// <summary>
    /// The entry of a book told in several files: the folder that holds them, whether the search found the folder or
    /// one of its files. Null for anything else.
    /// </summary>
    private LocalCatalogEntry? ToAudiobookEntry(BaseItem item)
    {
        var folder = item switch
        {
            AudioBook file => file.GetParent(),
            Folder found => found,
            _ => null
        };
        if (folder is null)
        {
            return null;
        }

        var parts = AudiobookFolders.GetParts(folder, _libraryManager);
        if (parts.Count == 0)
        {
            return null;
        }

        return new LocalCatalogEntry
        {
            ItemId = folder.Id,
            Kind = CatalogItemKind.Audiobook,
            Title = folder.Name,
            Year = parts[0].ProductionYear,
            Overview = parts[0].Overview,
            Asin = FindAsin(parts[0]),
            Authors = AudiobookFolders.GetAuthors(folder, parts)
        };
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<int, LocalCatalogEntry> FindByTmdbIds(CatalogItemKind kind, IReadOnlyCollection<int> tmdbIds, Guid userId)
    {
        if (tmdbIds.Count == 0)
        {
            return new Dictionary<int, LocalCatalogEntry>();
        }

        // People are not attached to a library, so they are not filtered by user.
        var user = kind == CatalogItemKind.Person ? null : _userManager.GetUserById(userId);
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [ToItemKind(kind)],
            HasAnyProviderIds = new Dictionary<string, string[]>
            {
                [MetadataProvider.Tmdb.ToString()] = tmdbIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray()
            },
            Recursive = true,
            DtoOptions = ProviderIdsOnly()
        };

        var result = new Dictionary<int, LocalCatalogEntry>();
        foreach (var item in _libraryManager.GetItemList(query))
        {
            if (ToEntry(item) is { TmdbId: int tmdbId } entry)
            {
                result.TryAdd(tmdbId, entry);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public LocalSeriesContent GetSeriesContent(int seriesTmdbId, Guid userId)
    {
        // Without a user there is nothing to filter by, and an unfiltered answer would bypass parental control.
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return LocalSeriesContent.Empty;
        }

        var series = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Series],
            HasAnyProviderIds = new Dictionary<string, string[]>
            {
                [MetadataProvider.Tmdb.ToString()] = [seriesTmdbId.ToString(CultureInfo.InvariantCulture)]
            },
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        });
        if (series.Count == 0)
        {
            return LocalSeriesContent.Empty;
        }

        // The user filter applies to each child too: a season or an episode above the user's rating is left out.
        var children = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Season, BaseItemKind.Episode],
            AncestorIds = series.Select(s => s.Id).ToArray(),
            IsVirtualItem = false,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        });

        var seasons = children.OfType<Season>()
            .Where(s => s.IndexNumber is not null)
            .Select(s => new LocalSeasonEntry { ItemId = s.Id, SeasonNumber = s.IndexNumber!.Value, Name = s.Name })
            .ToList();
        var seasonNumbers = seasons.ToDictionary(s => s.ItemId, s => s.SeasonNumber);

        var episodes = new List<LocalEpisodeEntry>();
        foreach (var episode in children.OfType<Episode>())
        {
            // An episode that cannot be placed in a season cannot be shown in a season either.
            int? seasonNumber = episode.ParentIndexNumber ?? (seasonNumbers.TryGetValue(episode.SeasonId, out var number) ? number : null);
            if (seasonNumber is null)
            {
                continue;
            }

            episodes.Add(new LocalEpisodeEntry
            {
                ItemId = episode.Id,
                SeasonNumber = seasonNumber.Value,
                EpisodeNumber = episode.IndexNumber,
                EpisodeNumberEnd = episode.IndexNumberEnd,
                Title = episode.Name,
                Overview = episode.Overview,
                AirDate = episode.PremiereDate is { } premiere ? DateOnly.FromDateTime(premiere) : null,
                RuntimeMinutes = episode.RunTimeTicks is > 0 and var ticks ? (int)Math.Round(TimeSpan.FromTicks(ticks).TotalMinutes) : null
            });
        }

        return new LocalSeriesContent { SeriesItemId = series[0].Id, Seasons = seasons, Episodes = episodes };
    }

    /// <inheritdoc />
    public IReadOnlySet<int> GetPlayedMovieTmdbIds(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return new HashSet<int>();
        }

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsPlayed = true,
            Recursive = true,
            DtoOptions = ProviderIdsOnly()
        };

        return _libraryManager.GetItemList(query)
            .Select(i => ParseTmdbId(i))
            .OfType<int>()
            .ToHashSet();
    }

    /// <summary>
    /// Options for the items read for their TMDB id. The repository loads provider ids only when the field is asked
    /// for: without it the item matches the query but comes back without its TMDB id, and is taken for missing.
    /// </summary>
    /// <returns>The stored columns and the provider ids, nothing else.</returns>
    private static DtoOptions ProviderIdsOnly()
    {
        var options = DtoOptions.StoredColumnsOnly;
        options.Fields = [ItemFields.ProviderIds];
        return options;
    }

    private static BaseItemKind ToItemKind(CatalogItemKind kind) => kind switch
    {
        CatalogItemKind.Movie => BaseItemKind.Movie,
        CatalogItemKind.Series => BaseItemKind.Series,
        CatalogItemKind.Person => BaseItemKind.Person,
        CatalogItemKind.Audiobook => BaseItemKind.AudioBook,
        CatalogItemKind.Book => BaseItemKind.Book,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static LocalCatalogEntry? ToEntry(BaseItem item)
    {
        CatalogItemKind kind;
        IReadOnlyList<string> authors = [];
        switch (item)
        {
            case Movie:
                kind = CatalogItemKind.Movie;
                break;
            case Series:
                kind = CatalogItemKind.Series;
                break;
            case Person:
                kind = CatalogItemKind.Person;
                break;
            case AudioBook audioBook:
                kind = CatalogItemKind.Audiobook;
                authors = audioBook.AlbumArtists.Concat(audioBook.Artists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                break;
            case Book:
                kind = CatalogItemKind.Book;
                break;
            default:
                return null;
        }

        var asin = FindAsin(item);

        return new LocalCatalogEntry
        {
            ItemId = item.Id,
            Kind = kind,
            Title = item.Name,
            OriginalTitle = string.IsNullOrEmpty(item.OriginalTitle) ? null : item.OriginalTitle,
            Year = item.ProductionYear,
            Overview = item.Overview,
            TmdbId = ParseTmdbId(item),
            Asin = asin,
            Authors = authors
        };
    }

    private static int? ParseTmdbId(BaseItem item)
        => item.TryGetProviderId(MetadataProvider.Tmdb, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;

    private static string? FindAsin(BaseItem item)
    {
        foreach (var key in _asinProviderKeys)
        {
            if (item.TryGetProviderId(key, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
