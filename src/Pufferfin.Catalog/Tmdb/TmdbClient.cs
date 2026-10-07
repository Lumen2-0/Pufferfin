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
    private const string DiscoverQualityFilters = "&vote_count.gte=50";

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
        CancellationToken cancellationToken)
    {
        var settings = GetEnabledSettings();
        var filters = DiscoverQualityFilters + (kind == CatalogItemKind.Movie ? DiscoverMovieRatingFilters : string.Empty);
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
            $"discover/{(kind == CatalogItemKind.Series ? "tv" : "movie")}?sort_by=popularity.desc&include_adult=false{filters}&page={page}&language={Uri.EscapeDataString(settings.PrimaryTmdbLanguage)}");
        return GetAsync<TmdbModels.ResultPage<TmdbModels.Entry>>(settings, path, CatalogCacheDurations.Discover, cancellationToken);
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
