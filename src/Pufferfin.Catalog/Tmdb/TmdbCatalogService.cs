using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// TMDB catalog: search, detailed sheets, credits, collections, recommendations, trailers and genres,
/// each title marked with its availability in the local library.
/// </summary>
public sealed class TmdbCatalogService
{
    private const int MaxCast = 20;
    private const int MaxRecommendations = 20;

    // TMDB returns 20 entries per page: 3 pages cover the largest limit of the API.
    private const int MaxTrendingPages = 3;

    private static readonly HashSet<string> _keyCrewJobs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Director", "Screenplay", "Writer", "Novel", "Creator", "Original Music Composer"
    };

    private static readonly HashSet<string> _trailerTypes = new(StringComparer.OrdinalIgnoreCase) { "Trailer", "Teaser" };

    private readonly TmdbClient _client;
    private readonly LocalAvailability _availability;
    private readonly ICatalogSettingsProvider _settingsProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbCatalogService"/> class.
    /// </summary>
    /// <param name="client">The TMDB client.</param>
    /// <param name="availability">The local availability lookup.</param>
    /// <param name="settingsProvider">The catalog settings.</param>
    public TmdbCatalogService(TmdbClient client, LocalAvailability availability, ICatalogSettingsProvider settingsProvider)
    {
        _client = client;
        _availability = availability;
        _settingsProvider = settingsProvider;
    }

    /// <summary>
    /// Searches films, series and people. The entries are not yet marked with their availability.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entries, by TMDB relevance.</returns>
    public async Task<IReadOnlyList<CatalogItem>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var page = await _client.SearchMultiAsync(query, cancellationToken).ConfigureAwait(false);
        return page?.Results.Select(e => TmdbMapper.ToItem(e)).OfType<CatalogItem>().ToList() ?? [];
    }

    /// <summary>
    /// Gets the sheet of a film.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sheet, or <c>null</c> when TMDB does not know the id.</returns>
    public async Task<CatalogTitleDetails?> GetMovieAsync(int tmdbId, Guid userId, CancellationToken cancellationToken)
    {
        var details = await _client.GetMovieAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        return details is null ? null : ToDetails(details, CatalogItemKind.Movie, userId);
    }

    /// <summary>
    /// Gets the sheet of a series.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sheet, or <c>null</c> when TMDB does not know the id.</returns>
    public async Task<CatalogTitleDetails?> GetSeriesAsync(int tmdbId, Guid userId, CancellationToken cancellationToken)
    {
        var details = await _client.GetSeriesAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        return details is null ? null : ToDetails(details, CatalogItemKind.Series, userId);
    }

    /// <summary>
    /// Gets the seasons of a series, each with the number of its episodes on disk.
    /// </summary>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The seasons, or <c>null</c> when TMDB does not know the id.</returns>
    public async Task<CatalogSeriesSeasons?> GetSeasonsAsync(int tmdbId, Guid userId, CancellationToken cancellationToken)
    {
        var series = await _client.GetSeriesSeasonsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return null;
        }

        var local = _availability.GetSeriesContent(tmdbId, userId);
        return new CatalogSeriesSeasons
        {
            TmdbId = tmdbId,
            Availability = local.SeriesItemId is null ? CatalogAvailability.Missing : CatalogAvailability.OnDisk,
            LocalItemId = local.SeriesItemId,
            Seasons = SeriesContentMerger.MergeSeasons(series.Seasons, local)
        };
    }

    /// <summary>
    /// Gets one season of a series with its episodes, TMDB merged with the disk.
    /// </summary>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <param name="seasonNumber">The season number (0 for specials).</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The season, or <c>null</c> when neither TMDB nor the disk knows it.</returns>
    public async Task<CatalogSeasonDetails?> GetSeasonAsync(int tmdbId, int seasonNumber, Guid userId, CancellationToken cancellationToken)
    {
        var season = await _client.GetSeasonAsync(tmdbId, seasonNumber, cancellationToken).ConfigureAwait(false);
        var local = _availability.GetSeriesContent(tmdbId, userId);
        var episodes = SeriesContentMerger.MergeEpisodes(seasonNumber, season?.Episodes ?? [], local.Episodes);
        if (season is null && episodes.Count == 0)
        {
            return null;
        }

        var folder = local.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNumber);
        var onDisk = episodes.Any(e => e.Availability == CatalogAvailability.OnDisk);
        return new CatalogSeasonDetails
        {
            SeriesTmdbId = tmdbId,
            SeasonNumber = seasonNumber,
            Name = string.IsNullOrWhiteSpace(season?.Name) ? folder?.Name : season.Name,
            Overview = string.IsNullOrWhiteSpace(season?.Overview) ? null : season.Overview,
            AirDate = TmdbMapper.ParseDate(season?.AirDate),
            PosterUrl = TmdbClient.ImageUrl(season?.PosterPath, "w500"),
            Availability = onDisk ? CatalogAvailability.OnDisk : CatalogAvailability.Missing,
            LocalItemId = folder?.ItemId,
            Sources = season is null ? [CatalogSource.Library] : onDisk ? [CatalogSource.Library, CatalogSource.Tmdb] : [CatalogSource.Tmdb],
            Episodes = episodes
        };
    }

    /// <summary>
    /// Gets the sheet of a person with their filmography.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sheet, or <c>null</c> when TMDB does not know the id.</returns>
    public async Task<CatalogPersonDetails?> GetPersonAsync(int tmdbId, Guid userId, CancellationToken cancellationToken)
    {
        var person = await _client.GetPersonAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        if (person is null)
        {
            return null;
        }

        var settings = _settingsProvider.GetSettings();
        var credits = (person.CombinedCredits?.Cast ?? []).Concat(person.CombinedCredits?.Crew ?? [])
            .Select(e => TmdbMapper.ToItem(e))
            .OfType<CatalogItem>()
            .Where(i => i.Kind is CatalogItemKind.Movie or CatalogItemKind.Series)
            .GroupBy(i => i.Key)
            .Select(g => g.First())
            .OrderByDescending(i => i.Year ?? int.MaxValue)
            .ToList();

        var biography = TmdbLocalization.Pick(person.Translations, settings.TmdbLanguages, "en", d => d.Biography, null, person.Biography);
        var local = _availability.Find(CatalogItemKind.Person, tmdbId, userId);
        return new CatalogPersonDetails
        {
            TmdbId = person.Id,
            Name = person.Name ?? string.Empty,
            Biography = biography.Value,
            Birthday = TmdbMapper.ParseDate(person.Birthday),
            Deathday = TmdbMapper.ParseDate(person.Deathday),
            PlaceOfBirth = person.PlaceOfBirth,
            KnownForDepartment = person.KnownForDepartment,
            ImageUrl = TmdbClient.ImageUrl(person.ProfilePath, "h632"),
            Filmography = _availability.Annotate(credits, userId),
            Availability = local is null ? CatalogAvailability.Missing : CatalogAvailability.OnDisk,
            LocalItemId = local?.ItemId
        };
    }

    /// <summary>
    /// Gets a film collection (saga).
    /// </summary>
    /// <param name="tmdbId">The TMDB collection id.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection, or <c>null</c> when TMDB does not know the id.</returns>
    public async Task<CatalogCollectionDetails?> GetCollectionAsync(int tmdbId, Guid userId, CancellationToken cancellationToken)
    {
        var collection = await _client.GetCollectionAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        if (collection is null)
        {
            return null;
        }

        var parts = collection.Parts
            .Select(e => TmdbMapper.ToItem(e, CatalogItemKind.Movie))
            .OfType<CatalogItem>()
            .OrderBy(i => i.Year ?? int.MaxValue)
            .ToList();
        return new CatalogCollectionDetails
        {
            TmdbId = collection.Id,
            Name = collection.Name ?? string.Empty,
            Overview = string.IsNullOrWhiteSpace(collection.Overview) ? null : collection.Overview,
            PosterUrl = TmdbClient.ImageUrl(collection.PosterPath, "w500"),
            BackdropUrl = TmdbClient.ImageUrl(collection.BackdropPath, "w1280"),
            Parts = _availability.Annotate(parts, userId)
        };
    }

    /// <summary>
    /// Gets the TMDB genres of films or series.
    /// </summary>
    /// <param name="kind">The kind: <see cref="CatalogItemKind.Movie"/> or <see cref="CatalogItemKind.Series"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The genres.</returns>
    public async Task<IReadOnlyList<CatalogGenre>> GetGenresAsync(CatalogItemKind kind, CancellationToken cancellationToken)
    {
        var list = await _client.GetGenresAsync(kind, cancellationToken).ConfigureAwait(false);
        return list?.Genres.Where(g => !string.IsNullOrEmpty(g.Name)).Select(g => new CatalogGenre(g.Id, g.Name!)).ToList() ?? [];
    }

    /// <summary>
    /// Gets the films or series trending on TMDB this week, each marked with its availability.
    /// </summary>
    /// <param name="kind">The kind: <see cref="CatalogItemKind.Movie"/> or <see cref="CatalogItemKind.Series"/>.</param>
    /// <param name="limit">The maximum number of entries.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entries, most popular first.</returns>
    public async Task<IReadOnlyList<CatalogItem>> GetTrendingAsync(CatalogItemKind kind, int limit, Guid userId, CancellationToken cancellationToken)
    {
        var items = new List<CatalogItem>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; page <= MaxTrendingPages && items.Count < limit; page++)
        {
            var result = await _client.GetTrendingAsync(kind, page, cancellationToken).ConfigureAwait(false);

            // Pages are cached separately, so a title can move from one page to the next between two fetches.
            items.AddRange(result?.Results
                .Select(e => TmdbMapper.ToItem(e, kind))
                .OfType<CatalogItem>()
                .Where(i => i.Kind == kind && keys.Add(i.Key)) ?? []);
            if (result is null || result.Results.Count == 0 || page >= result.TotalPages)
            {
                break;
            }
        }

        return _availability.Annotate(items.Take(limit).ToList(), userId);
    }

    /// <summary>
    /// Gets the TMDB recommendations of a film, without availability.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The recommended films, by relevance.</returns>
    public async Task<IReadOnlyList<CatalogItem>> GetMovieRecommendationsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var page = await _client.GetMovieRecommendationsAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        return page?.Results.Select(e => TmdbMapper.ToItem(e, CatalogItemKind.Movie)).OfType<CatalogItem>().ToList() ?? [];
    }

    private CatalogTitleDetails ToDetails(TmdbModels.TitleDetails details, CatalogItemKind kind, Guid userId)
    {
        var settings = _settingsProvider.GetSettings();
        var isMovie = kind == CatalogItemKind.Movie;
        var originalTitle = isMovie ? details.OriginalTitle : details.OriginalName;
        var title = TmdbLocalization.Pick(
            details.Translations,
            settings.TmdbLanguages,
            details.OriginalLanguage,
            d => isMovie ? d.Title : d.Name,
            originalTitle,
            isMovie ? details.Title : details.Name);
        var overview = TmdbLocalization.Pick(details.Translations, settings.TmdbLanguages, details.OriginalLanguage, d => d.Overview, null, details.Overview);
        var tagline = TmdbLocalization.Pick(details.Translations, settings.TmdbLanguages, details.OriginalLanguage, d => d.Tagline, null, details.Tagline);
        var releaseDate = TmdbMapper.ParseDate(isMovie ? details.ReleaseDate : details.FirstAirDate);

        var countries = details.OriginCountry.Count > 0
            ? details.OriginCountry.Select(code => new CatalogCountry(
                code,
                details.ProductionCountries.FirstOrDefault(c => string.Equals(c.Iso31661, code, StringComparison.OrdinalIgnoreCase))?.Name))
            : details.ProductionCountries.Where(c => !string.IsNullOrEmpty(c.Iso31661)).Select(c => new CatalogCountry(c.Iso31661!, c.Name));

        var crew = (details.Credits?.Crew ?? [])
            .Where(c => c.Job is not null && _keyCrewJobs.Contains(c.Job))
            .Concat(details.CreatedBy.Select(c => new TmdbModels.Entry { Id = c.Id, Name = c.Name, ProfilePath = c.ProfilePath, Job = "Creator" }));

        var recommendations = (details.Recommendations?.Results ?? [])
            .Select(e => TmdbMapper.ToItem(e, kind))
            .OfType<CatalogItem>()
            .Take(MaxRecommendations)
            .ToList();

        var local = _availability.Find(kind, details.Id, userId);
        return new CatalogTitleDetails
        {
            Kind = kind,
            TmdbId = details.Id,
            Title = title.Value ?? originalTitle ?? string.Empty,
            OriginalTitle = originalTitle,
            OriginalLanguage = details.OriginalLanguage,
            Overview = overview.Value,
            OverviewLanguage = overview.Language,
            Tagline = tagline.Value,
            ReleaseDate = releaseDate,
            Year = releaseDate?.Year,
            RuntimeMinutes = isMovie ? details.Runtime : details.EpisodeRunTime.FirstOrDefault() is var runtime and > 0 ? runtime : null,
            NumberOfSeasons = isMovie ? null : details.NumberOfSeasons,
            VoteAverage = details.VoteAverage,
            PosterUrl = TmdbClient.ImageUrl(details.PosterPath, "w500"),
            BackdropUrl = TmdbClient.ImageUrl(details.BackdropPath, "w1280"),
            ImdbId = details.ImdbId ?? details.ExternalIds?.ImdbId,
            TvdbId = isMovie ? null : details.ExternalIds?.TvdbId,
            Genres = details.Genres.Where(g => !string.IsNullOrEmpty(g.Name)).Select(g => new CatalogGenre(g.Id, g.Name!)).ToList(),
            Countries = countries.ToList(),
            Cast = (details.Credits?.Cast ?? []).Take(MaxCast).Select(c => ToCredit(c, "Acting")).ToList(),
            Crew = crew.GroupBy(c => (c.Id, c.Job)).Select(g => ToCredit(g.First(), null)).ToList(),
            Trailers = SelectTrailers(details.Videos?.Results ?? [], settings.TmdbLanguages, details.OriginalLanguage),
            Collection = details.BelongsToCollection is { } collection && !string.IsNullOrEmpty(collection.Name)
                ? new CatalogCollectionSummary(collection.Id, collection.Name, TmdbClient.ImageUrl(collection.PosterPath, "w500"))
                : null,
            Recommendations = _availability.Annotate(recommendations, userId),
            Availability = local is null ? CatalogAvailability.Missing : CatalogAvailability.OnDisk,
            LocalItemId = local?.ItemId
        };
    }

    private static CatalogCredit ToCredit(TmdbModels.Entry entry, string? department)
        => new()
        {
            TmdbId = entry.Id,
            Name = entry.Name ?? string.Empty,
            Role = !string.IsNullOrWhiteSpace(entry.Character) ? entry.Character : entry.Job,
            Department = department ?? entry.KnownForDepartment,
            ImageUrl = TmdbClient.ImageUrl(entry.ProfilePath, "w185")
        };

    /// <summary>
    /// Keeps trailers and teasers, preferred languages first, then the original language, official ones first.
    /// </summary>
    /// <param name="videos">The TMDB videos.</param>
    /// <param name="languages">The configured languages.</param>
    /// <param name="originalLanguage">The original language.</param>
    /// <returns>The ordered trailers.</returns>
    internal static IReadOnlyList<CatalogVideo> SelectTrailers(IReadOnlyList<TmdbModels.Video> videos, IReadOnlyList<string> languages, string? originalLanguage)
    {
        var order = TmdbClient.LanguageCodes(languages).ToList();
        if (originalLanguage is not null && !order.Contains(originalLanguage, StringComparer.OrdinalIgnoreCase))
        {
            order.Add(originalLanguage.ToLowerInvariant());
        }

        int Rank(TmdbModels.Video video)
        {
            var index = order.FindIndex(l => string.Equals(l, video.Iso6391, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? order.Count : index;
        }

        return videos
            .Where(v => !string.IsNullOrEmpty(v.Key) && !string.IsNullOrEmpty(v.Site) && v.Type is not null && _trailerTypes.Contains(v.Type))
            .OrderBy(Rank)
            .ThenBy(v => string.Equals(v.Type, "Trailer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(v => v.Official)
            .ThenByDescending(v => v.PublishedAt, StringComparer.Ordinal)
            .Select(v => new CatalogVideo
            {
                Name = v.Name ?? string.Empty,
                Site = v.Site!,
                Key = v.Key!,
                Type = v.Type!,
                Language = v.Iso6391,
                Official = v.Official,
                Url = v.Site switch
                {
                    "YouTube" => "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(v.Key!),
                    "Vimeo" => "https://vimeo.com/" + Uri.EscapeDataString(v.Key!),
                    _ => null
                }
            })
            .ToList();
    }
}
