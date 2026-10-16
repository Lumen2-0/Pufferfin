using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Caching;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// Low level TMDB v3 client: one cached call per method, raw payloads.
/// </summary>
/// <remarks>
/// The credential is added to the request only when it is sent. Cache keys, logs and exceptions are built from
/// the request path without it.
/// </remarks>
public sealed class TmdbClient
{
    /// <summary>
    /// The name of the HTTP client registered for TMDB.
    /// </summary>
    public const string HttpClientName = "Pufferfin.Tmdb";

    /// <summary>
    /// The TMDB API base address.
    /// </summary>
    public static readonly Uri BaseAddress = new("https://api.themoviedb.org/3/");

    private const string TitleAppend = "credits,videos,recommendations,translations,external_ids";

    // Discover rows sit on the home screen. TMDB does not flag every explicit title as adult, and raw popularity
    // lifts obscure ones to the top: a floor of votes keeps titles people actually rated, and films are capped at the
    // US rating R, which also leaves out unrated ones. TMDB has no rating filter for series.
    private const int DiscoverMinVotes = 50;

    private const string DiscoverMovieRatingFilters = "&certification_country=US&certification.lte=R";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICatalogCache _cache;
    private readonly CatalogRateLimiters _rateLimiters;
    private readonly ICatalogSettingsProvider _settingsProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="cache">The catalog cache.</param>
    /// <param name="rateLimiters">The outbound rate limiters.</param>
    /// <param name="settingsProvider">The catalog settings.</param>
    public TmdbClient(
        IHttpClientFactory httpClientFactory,
        ICatalogCache cache,
        CatalogRateLimiters rateLimiters,
        ICatalogSettingsProvider settingsProvider)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _rateLimiters = rateLimiters;
        _settingsProvider = settingsProvider;
    }

    internal Task<TmdbModels.ResultPage<TmdbModels.Entry>?> SearchMultiAsync(string query, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"search/multi?query={Uri.EscapeDataString(query.ToLowerInvariant())}&include_adult=false&page=1&language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.ResultPage<TmdbModels.Entry>>(settings, path, CatalogCacheDurations.Search, cancellationToken);
    }

    internal Task<TmdbModels.TitleDetails?> GetMovieAsync(int id, CancellationToken cancellationToken)
        => GetTitleAsync("movie", id, cancellationToken);

    internal Task<TmdbModels.TitleDetails?> GetSeriesAsync(int id, CancellationToken cancellationToken)
        => GetTitleAsync("tv", id, cancellationToken);

    internal Task<TmdbModels.SeriesSeasons?> GetSeriesSeasonsAsync(int seriesId, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"tv/{seriesId}?language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.SeriesSeasons>(settings, path, CatalogCacheDurations.Details, cancellationToken);
    }

    internal Task<TmdbModels.SeasonDetails?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"tv/{seriesId}/season/{seasonNumber}?language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.SeasonDetails>(settings, path, CatalogCacheDurations.Details, cancellationToken);
    }

    internal Task<TmdbModels.PersonDetails?> GetPersonAsync(int id, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"person/{id}?append_to_response=combined_credits,translations&language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.PersonDetails>(settings, path, CatalogCacheDurations.Details, cancellationToken);
    }

    internal Task<TmdbModels.CollectionDetails?> GetCollectionAsync(int id, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"collection/{id}?language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.CollectionDetails>(settings, path, CatalogCacheDurations.Details, cancellationToken);
    }

    internal Task<TmdbModels.GenreList?> GetGenresAsync(CatalogItemKind kind, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"genre/{(kind == CatalogItemKind.Series ? "tv" : "movie")}/list?language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.GenreList>(settings, path, CatalogCacheDurations.Genres, cancellationToken);
    }

    internal Task<TmdbModels.ResultPage<TmdbModels.Entry>?> GetMovieRecommendationsAsync(int id, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"movie/{id}/recommendations?page=1&language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.ResultPage<TmdbModels.Entry>>(settings, path, CatalogCacheDurations.Details, cancellationToken);
    }

    internal Task<TmdbModels.ResultPage<TmdbModels.Entry>?> GetTrendingAsync(CatalogItemKind kind, int page, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"trending/{(kind == CatalogItemKind.Series ? "tv" : "movie")}/week?page={page}&language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.ResultPage<TmdbModels.Entry>>(settings, path, CatalogCacheDurations.Trending, cancellationToken);
    }

    internal Task<TmdbModels.ResultPage<TmdbModels.Entry>?> DiscoverAsync(
        CatalogItemKind kind,
        int? genreId,
        IReadOnlyCollection<TmdbSeriesType> seriesTypes,
        int page,
        CancellationToken cancellationToken,
        TmdbDiscoverOptions? options = null)
    {
        var settings = GetEnabledSettings();
        options ??= new TmdbDiscoverOptions();
        var isMovie = kind == CatalogItemKind.Movie;
        var filters = DiscoverFilters(isMovie, options, settings.WatchRegion) + (isMovie ? DiscoverMovieRatingFilters : string.Empty);
        if (genreId is { } genre)
        {
            filters += string.Create(CultureInfo.InvariantCulture, $"&with_genres={genre}");
        }

        if (seriesTypes.Count > 0)
        {
            // TMDB reads "|" as OR between the types; ordered so the same set always hits the same cache entry.
            filters += "&with_type=" + string.Join("%7C", seriesTypes.Distinct().Order().Select(t => ((int)t).ToString(CultureInfo.InvariantCulture)));
        }

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"discover/{(kind == CatalogItemKind.Series ? "tv" : "movie")}?include_adult=false{filters}&page={page}&language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.ResultPage<TmdbModels.Entry>>(settings, path, CatalogCacheDurations.Discover, cancellationToken);
    }

    /// <summary>
    /// Builds the order and the filters of a Discover list: a vote floor that keeps the list presentable, higher for the
    /// best rated (a handful of votes would put unknown titles first), lower for a single year and for the latest out.
    /// </summary>
    /// <param name="isMovie">Films rather than series.</param>
    /// <param name="options">The order, the year, the original language and the brand.</param>
    /// <param name="watchRegion">The country of the streaming offers.</param>
    /// <returns>The query string parts, each starting with <c>&amp;</c>.</returns>
    internal static string DiscoverFilters(bool isMovie, TmdbDiscoverOptions options, string watchRegion = "US")
    {
        var dateField = isMovie ? "primary_release_date" : "first_air_date";
        int minVotes;
        string sort;
        switch (options.Sort)
        {
            case TmdbDiscoverSort.TopRated:
                sort = "vote_average.desc";
                minVotes = options.Year is null ? (isMovie ? 1000 : 300) : (isMovie ? 150 : 60);
                break;
            case TmdbDiscoverSort.Newest:
                sort = dateField + ".desc";
                minVotes = 10;
                break;
            default:
                sort = "popularity.desc";
                minVotes = DiscoverMinVotes;
                break;
        }

        var filters = string.Create(CultureInfo.InvariantCulture, $"&sort_by={sort}&vote_count.gte={minVotes}");
        if (options.Sort == TmdbDiscoverSort.Newest)
        {
            // Not the announced ones: only what is out today
            filters += "&" + dateField + ".lte=" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (options.Year is { } year)
        {
            filters += string.Create(CultureInfo.InvariantCulture, $"&{(isMovie ? "primary_release_year" : "first_air_date_year")}={year}");
        }

        if (options.Since is { } since)
        {
            filters += "&" + dateField + ".gte=" + since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(options.OriginalLanguage))
        {
            filters += "&with_original_language=" + Uri.EscapeDataString(options.OriginalLanguage.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrEmpty(options.Keywords))
        {
            // A pipe is an OR for TMDB, a comma an AND
            filters += "&with_keywords=" + options.Keywords.Replace(',', '|');
        }

        if (options.Brand is { } brand)
        {
            // TMDB reads "|" as OR between the ids
            var ids = string.Join("%7C", brand.Ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            filters += brand.Kind switch
            {
                // In the subscription of the service, in the country of the offers: not to rent or to buy there
                DiscoverBrandKind.Service => "&with_watch_providers=" + ids + "&watch_region=" + Uri.EscapeDataString(brand.Region ?? watchRegion) + "&with_watch_monetization_types=flatrate",
                DiscoverBrandKind.Network => "&with_networks=" + ids,
                _ => "&with_companies=" + ids
            };
        }

        return filters;
    }

    /// <summary>
    /// Builds the absolute URL of a TMDB image.
    /// </summary>
    /// <param name="path">The image path returned by TMDB.</param>
    /// <param name="size">The TMDB size (<c>w500</c>, <c>w1280</c>, <c>w185</c>).</param>
    /// <returns>The URL, or <c>null</c> when there is no image.</returns>
    internal static string? ImageUrl(string? path, string size)
        => string.IsNullOrEmpty(path) ? null : "https://image.tmdb.org/t/p/" + size + path;

    /// <summary>
    /// Returns the ISO 639-1 part of the configured languages, for <c>include_video_language</c>.
    /// </summary>
    /// <param name="languages">The configured languages.</param>
    /// <returns>The distinct language codes.</returns>
    internal static IEnumerable<string> LanguageCodes(IEnumerable<string> languages)
        => languages.Select(l => l.Split('-')[0].ToLowerInvariant()).Distinct(StringComparer.Ordinal);

    private Task<TmdbModels.TitleDetails?> GetTitleAsync(string type, int id, CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var videoLanguages = string.Join(',', LanguageCodes(settings.TmdbLanguages).Append("en").Append("null").Distinct(StringComparer.Ordinal));
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{type}/{id}?append_to_response={TitleAppend}&language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}&include_video_language={videoLanguages}");
        return GetAsync<TmdbModels.TitleDetails>(settings, path, CatalogCacheDurations.Details, cancellationToken);
    }

    private CatalogSettings GetEnabledSettings()
    {
        var settings = _settingsProvider.GetSettings();
        if (!settings.IsTmdbEnabled)
        {
            throw new CatalogSourceException(CatalogSource.Tmdb, CatalogSourceStatus.Disabled);
        }

        return settings;
    }

    private Task<T?> GetAsync<T>(CatalogSettings settings, string path, TimeSpan timeToLive, CancellationToken cancellationToken)
        where T : class
        => _cache.GetOrCreateAsync<T>(
            "tmdb:" + path,
            timeToLive,
            async ct =>
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                var body = await CatalogHttp.GetStringAsync(
                    client,
                    () => CreateRequest(settings.TmdbApiKey!, path),
                    CatalogSource.Tmdb,
                    _rateLimiters.Tmdb,
                    ct).ConfigureAwait(false);
                if (body is null)
                {
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(body, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new CatalogSourceException(CatalogSource.Tmdb, CatalogSourceStatus.Unavailable, ex);
                }
            },
            cancellationToken);

    private static HttpRequestMessage CreateRequest(string credential, string path)
    {
        // A v4 read access token is a JWT; anything else is a v3 API key, which TMDB only accepts in the query string.
        var isAccessToken = credential.Count(c => c == '.') == 2;
        var uri = isAccessToken ? path : path + "&api_key=" + Uri.EscapeDataString(credential);
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, uri));
        if (isAccessToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        return request;
    }
}
