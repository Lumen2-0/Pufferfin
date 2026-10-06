using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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
        var itemKinds = kinds.Select(ToItemKind).ToArray();
        if (itemKinds.Length == 0)
        {
            return [];
        }

        var result = await _searchManager.GetSearchHintsAsync(
            new SearchQuery
            {
                UserId = userId,
                SearchTerm = query,
                Limit = limit,
                IncludeItemTypes = itemKinds,
                IncludePeople = kinds.Contains(CatalogItemKind.Person),
                IncludeMedia = true,
                IncludeGenres = false,
                IncludeStudios = false,
                IncludeArtists = false
            },
            cancellationToken).ConfigureAwait(false);

        return result.Items
            .Select(hint => ToEntry(hint.Item))
            .OfType<LocalCatalogEntry>()
            .ToList();
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
            DtoOptions = new DtoOptions(false)
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
            DtoOptions = new DtoOptions(false)
        };

        return _libraryManager.GetItemList(query)
            .Select(i => ParseTmdbId(i))
            .OfType<int>()
            .ToHashSet();
    }

    private static BaseItemKind ToItemKind(CatalogItemKind kind) => kind switch
    {
        CatalogItemKind.Movie => BaseItemKind.Movie,
        CatalogItemKind.Series => BaseItemKind.Series,
        CatalogItemKind.Person => BaseItemKind.Person,
        CatalogItemKind.Audiobook => BaseItemKind.AudioBook,
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
            default:
                return null;
        }

        string? asin = null;
        foreach (var key in _asinProviderKeys)
        {
            if (item.TryGetProviderId(key, out var value))
            {
                asin = value;
                break;
            }
        }

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
}
