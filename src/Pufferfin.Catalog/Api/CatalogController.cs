using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.ModelBinders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Books;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Recommendations;
using Pufferfin.Catalog.Search;
using Pufferfin.Catalog.Tmdb;

namespace Pufferfin.Catalog.Api;

/// <summary>
/// The Pufferfin catalog: universal search, TMDB sheets and trending lists, Audible search, book search and recommendations.
/// </summary>
/// <remarks>
/// Failures of an external catalog are reported as a <see cref="CatalogSourceReport"/>: never a URL or a credential.
/// </remarks>
[Authorize]
[Route("Pufferfin/Catalog")]
[Tags("PufferfinCatalog")]
public partial class CatalogController : BaseJellyfinApiController
{
    private const int DefaultSuggestionLimit = 8;

    private const int MaxDiscoverPage = 500;

    private readonly UniversalSearchService _search;
    private readonly TmdbCatalogService _tmdb;
    private readonly AudibleCatalogService _audible;
    private readonly ILocalCatalogIndex _index;
    private readonly RecommendationService _recommendations;
    private readonly SearchRateLimiter _rateLimiter;
    private readonly IBookCatalog _books;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogController"/> class.
    /// </summary>
    /// <param name="search">The universal search.</param>
    /// <param name="tmdb">The TMDB catalog.</param>
    /// <param name="audible">The Audible catalog.</param>
    /// <param name="index">The local library index.</param>
    /// <param name="recommendations">The recommendations.</param>
    /// <param name="rateLimiter">The per user search rate limit.</param>
    /// <param name="books">The book catalog.</param>
    public CatalogController(
        UniversalSearchService search,
        TmdbCatalogService tmdb,
        AudibleCatalogService audible,
        ILocalCatalogIndex index,
        RecommendationService recommendations,
        SearchRateLimiter rateLimiter,
        IBookCatalog books)
    {
        _search = search;
        _tmdb = tmdb;
        _audible = audible;
        _index = index;
        _recommendations = recommendations;
        _rateLimiter = rateLimiter;
        _books = books;
    }

    /// <summary>
    /// Searches the local library, TMDB and Audible at once.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="kinds">Optional. The kinds to search, comma delimited (Movie, Series, Person, Audiobook). Default: all.</param>
    /// <param name="limit">Optional. The maximum number of entries (1 to 50, default 20).</param>
    /// <param name="audiobookLanguage">Optional. The audiobook language to keep (<c>fr</c>, <c>en</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">One entry per title or person, each marked on disk or missing, with the status of every source.</response>
    /// <response code="429">The user sent too many searches; retry after the Retry-After delay.</response>
    /// <returns>The merged results.</returns>
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<CatalogSearchResponse>> Search(
        [FromQuery, Required] string query,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] CatalogItemKind[] kinds,
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit,
        [FromQuery] string? audiobookLanguage,
        CancellationToken cancellationToken)
        => RunSearchAsync(
            new CatalogSearchRequest { Query = query, Kinds = kinds, Limit = limit ?? 20, AudiobookLanguage = audiobookLanguage },
            cancellationToken);

    /// <summary>
    /// Type-ahead suggestions: the universal search with a short deadline per source and few entries.
    /// </summary>
    /// <param name="query">The text typed so far (at least 2 characters to query the sources).</param>
    /// <param name="kinds">Optional. The kinds to search, comma delimited. Default: all.</param>
    /// <param name="limit">Optional. The maximum number of entries (default 8).</param>
    /// <param name="audiobookLanguage">Optional. The audiobook language to keep.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The suggestions.</response>
    /// <response code="429">The user sent too many searches; retry after the Retry-After delay.</response>
    /// <returns>The merged suggestions.</returns>
    [HttpGet("Suggest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<CatalogSearchResponse>> Suggest(
        [FromQuery, Required] string query,
        [FromQuery, ModelBinder(typeof(CommaDelimitedCollectionModelBinder))] CatalogItemKind[] kinds,
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit,
        [FromQuery] string? audiobookLanguage,
        CancellationToken cancellationToken)
        => RunSearchAsync(
            new CatalogSearchRequest
            {
                Query = query,
                Kinds = kinds,
                Limit = limit ?? DefaultSuggestionLimit,
                AudiobookLanguage = audiobookLanguage,
                IsSuggestion = true
            },
            cancellationToken);

    /// <summary>
    /// Gets the sheet of a film: credits, collection, recommendations, trailers, genres and countries.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The sheet.</response>
    /// <response code="404">TMDB does not know this film.</response>
    /// <response code="503">TMDB is not configured or not reachable.</response>
    /// <returns>The sheet, or the failure.</returns>
    [HttpGet("Movies/{tmdbId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<CatalogTitleDetails>> GetMovie([FromRoute, Range(1, int.MaxValue)] int tmdbId, CancellationToken cancellationToken)
        => RunSourceAsync(() => _tmdb.GetMovieAsync(tmdbId, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Gets the sheet of a series: credits, recommendations, trailers, genres and countries.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The sheet.</response>
    /// <response code="404">TMDB does not know this series.</response>
    /// <response code="503">TMDB is not configured or not reachable.</response>
    /// <returns>The sheet, or the failure.</returns>
    [HttpGet("Series/{tmdbId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<CatalogTitleDetails>> GetSeries([FromRoute, Range(1, int.MaxValue)] int tmdbId, CancellationToken cancellationToken)
        => RunSourceAsync(() => _tmdb.GetSeriesAsync(tmdbId, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Gets the seasons of a series, TMDB merged with the local library: the episodes announced and those on disk.
    /// </summary>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">One entry per season, with the number of its episodes on disk for the current user. A season on disk that TMDB does not list is included, its sources limited to the library.</response>
    /// <response code="404">TMDB does not know this series.</response>
    /// <response code="503">TMDB is not configured or not reachable.</response>
    /// <returns>The seasons, or the failure.</returns>
    [HttpGet("Series/{tmdbId}/Seasons")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<CatalogSeriesSeasons>> GetSeriesSeasons([FromRoute, Range(1, int.MaxValue)] int tmdbId, CancellationToken cancellationToken)
        => RunSourceAsync(() => _tmdb.GetSeasonsAsync(tmdbId, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Gets the episodes of one season, TMDB merged with the local library.
    /// </summary>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <param name="seasonNumber">The season number (0 for specials).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The episodes, each once: on disk with its library item for the current user, or missing. An episode on disk that TMDB does not list is included, its sources limited to the library.</response>
    /// <response code="404">Neither TMDB nor the library of the current user knows this season.</response>
    /// <response code="503">TMDB is not configured or not reachable.</response>
    /// <returns>The season, or the failure.</returns>
    [HttpGet("Series/{tmdbId}/Seasons/{seasonNumber}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<CatalogSeasonDetails>> GetSeriesSeason(
        [FromRoute, Range(1, int.MaxValue)] int tmdbId,
        [FromRoute, Range(0, int.MaxValue)] int seasonNumber,
        CancellationToken cancellationToken)
        => RunSourceAsync(() => _tmdb.GetSeasonAsync(tmdbId, seasonNumber, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Gets the sheet of a person with their filmography.
    /// </summary>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The sheet.</response>
    /// <response code="404">TMDB does not know this person.</response>
    /// <response code="503">TMDB is not configured or not reachable.</response>
    /// <returns>The sheet, or the failure.</returns>
    [HttpGet("People/{tmdbId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<CatalogPersonDetails>> GetPerson([FromRoute, Range(1, int.MaxValue)] int tmdbId, CancellationToken cancellationToken)
        => RunSourceAsync(() => _tmdb.GetPersonAsync(tmdbId, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Gets a film collection (saga).
    /// </summary>
    /// <param name="tmdbId">The TMDB collection id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The collection.</response>
    /// <response code="404">TMDB does not know this collection.</response>
    /// <response code="503">TMDB is not configured or not reachable.</response>
    /// <returns>The collection, or the failure.</returns>
    [HttpGet("Collections/{tmdbId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ActionResult<CatalogCollectionDetails>> GetCollection([FromRoute, Range(1, int.MaxValue)] int tmdbId, CancellationToken cancellationToken)
        => RunSourceAsync(() => _tmdb.GetCollectionAsync(tmdbId, User.GetUserId(), cancellationToken));

    /// <summary>
    /// Gets the TMDB genres of films or series.
    /// </summary>
    /// <param name="kind">Movie or Series.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The genres.</response>
    /// <response code="400">The kind is not Movie or Series.</response>
    /// <response code="503">TMDB is not configured or not reachable.</response>
    /// <returns>The genres, or the failure.</returns>
    [HttpGet("Genres")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<CatalogGenre>>> GetGenres([FromQuery] CatalogItemKind kind = CatalogItemKind.Movie, CancellationToken cancellationToken = default)
    {
        if (kind is not (CatalogItemKind.Movie or CatalogItemKind.Series))
        {
            return BadRequest();
        }

        return await RunSourceAsync<IReadOnlyList<CatalogGenre>>(async () => await _tmdb.GetGenresAsync(kind, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the films or series trending on TMDB this week, for the "Trending" row of the home screen.
    /// </summary>
    /// <param name="kind">Movie or Series.</param>
    /// <param name="limit">Optional. The maximum number of entries (1 to 50, default 20).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The entries, most popular first, each marked on disk or missing, with the status of TMDB. Empty when TMDB is not configured or not reachable.</response>
    /// <response code="400">The kind is not Movie or Series.</response>
    /// <returns>The trending titles.</returns>
    [HttpGet("Trending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CatalogListResponse>> GetTrending(
        [FromQuery] CatalogItemKind kind = CatalogItemKind.Movie,
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (kind is not (CatalogItemKind.Movie or CatalogItemKind.Series))
        {
            return BadRequest();
        }

        try
        {
            var items = await _tmdb.GetTrendingAsync(kind, limit ?? 20, User.GetUserId(), cancellationToken).ConfigureAwait(false);
            return new CatalogListResponse { Items = items, Sources = [new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Ok)] };
        }
        catch (CatalogSourceException ex)
        {
            return new CatalogListResponse { Items = [], Sources = [new CatalogSourceReport(CatalogSource.Tmdb, ex.Status)] };
        }
    }

    /// <summary>
    /// Gets the films or series TMDB lists for a genre or a series type, most popular first, for the rows of the Discover screen.
    /// </summary>
    /// <remarks>
    /// The rows are shown on the home screen: titles with fewer than 50 TMDB votes are left out, and so are films rated above
    /// R in the US or not rated there.
    /// </remarks>
    /// <param name="kind">Movie or Series.</param>
    /// <param name="genreId">Optional. The TMDB genre id to keep (99 for documentaries), as listed by <c>Genres</c>.</param>
    /// <param name="tvTypes">Optional. Series only. The TMDB series types to keep, comma delimited, any of them matching:
    /// <c>Documentary</c>, <c>News</c>, <c>Miniseries</c>, <c>Reality</c>, <c>Scripted</c>, <c>TalkShow</c>, <c>Video</c>.</param>
    /// <param name="limit">Optional. The maximum number of entries, which is also the size of a page (1 to 50, default 20).</param>
    /// <param name="page">Optional. The page, from 1 (1 to 500, default 1).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The entries, most popular first, each marked on disk or missing, with the status of TMDB. Empty when TMDB is not configured or not reachable, or past the last page.</response>
    /// <response code="400">The kind is not Movie or Series, a series type is unknown, or series types are given for films.</response>
    /// <returns>The titles.</returns>
    [HttpGet("Discover")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CatalogListResponse>> GetDiscover(
        [FromQuery, Required] CatalogItemKind? kind,
        [FromQuery, Range(1, int.MaxValue)] int? genreId = null,
        [FromQuery] string? tvTypes = null,
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit = null,
        [FromQuery, Range(1, MaxDiscoverPage)] int? page = null,
        CancellationToken cancellationToken = default)
    {
        if (kind is not (CatalogItemKind.Movie or CatalogItemKind.Series)
            || !TryParseSeriesTypes(tvTypes, out var seriesTypes)
            || (kind == CatalogItemKind.Movie && seriesTypes.Count > 0))
        {
            return BadRequest();
        }

        try
        {
            var items = await _tmdb.DiscoverAsync(kind.Value, genreId, seriesTypes, limit ?? 20, page ?? 1, User.GetUserId(), cancellationToken).ConfigureAwait(false);
            return new CatalogListResponse { Items = items, Sources = [new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Ok)] };
        }
        catch (CatalogSourceException ex)
        {
            return new CatalogListResponse { Items = [], Sources = [new CatalogSourceReport(CatalogSource.Tmdb, ex.Status)] };
        }
    }

    /// <summary>
    /// Searches audiobooks by title, author or series in the configured Audible markets.
    /// </summary>
    /// <param name="keywords">Optional. Free text.</param>
    /// <param name="title">Optional. The title.</param>
    /// <param name="author">Optional. The author.</param>
    /// <param name="series">Optional. The series.</param>
    /// <param name="language">Optional. The language to keep (<c>fr</c>, <c>en</c>).</param>
    /// <param name="limit">Optional. The maximum number of entries (default 20).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The audiobooks, each marked on disk or missing, with the status of the sources.</response>
    /// <response code="400">No criterion was given.</response>
    /// <response code="429">The user sent too many searches; retry after the Retry-After delay.</response>
    /// <returns>The audiobooks.</returns>
    [HttpGet("Audiobooks")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CatalogListResponse>> SearchAudiobooks(
        [FromQuery] string? keywords,
        [FromQuery] string? title,
        [FromQuery] string? author,
        [FromQuery] string? series,
        [FromQuery] string? language,
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit,
        CancellationToken cancellationToken)
    {
        var query = new AudibleQuery
        {
            Keywords = Clean(keywords),
            Title = Clean(title),
            Author = Clean(author),
            Series = Clean(series),
            Language = language,
            Limit = limit ?? 20
        };
        if (!query.HasCriteria)
        {
            return BadRequest();
        }

        var userId = User.GetUserId();
        if (TooManyRequests(userId) is { } refused)
        {
            return refused;
        }

        var localText = (query.Title ?? query.Keywords ?? query.Series ?? query.Author)!;
        var local = await _index.SearchAsync(localText, userId, [CatalogItemKind.Audiobook], query.Limit, cancellationToken).ConfigureAwait(false);
        try
        {
            var audiobooks = await _audible.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            return new CatalogListResponse
            {
                Items = CatalogMerger.Merge(local, [], audiobooks, query.Limit),
                Sources = [new CatalogSourceReport(CatalogSource.Library, CatalogSourceStatus.Ok), new CatalogSourceReport(CatalogSource.Audible, CatalogSourceStatus.Ok)]
            };
        }
        catch (CatalogSourceException ex)
        {
            return new CatalogListResponse
            {
                Items = CatalogMerger.Merge(local, [], [], query.Limit),
                Sources = [new CatalogSourceReport(CatalogSource.Library, CatalogSourceStatus.Ok), new CatalogSourceReport(CatalogSource.Audible, ex.Status)]
            };
        }
    }

    /// <summary>
    /// Gets the popular audiobooks of the configured Audible markets, for the rows of the Discover screen.
    /// </summary>
    /// <param name="sort">BestSellers or NewReleases (released books only).</param>
    /// <param name="limit">Optional. The maximum number of entries (1 to 50, default 20).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The audiobooks, in the preferred audio languages when some are set, each marked on disk or missing, with the status of Audible.</response>
    /// <returns>The audiobooks.</returns>
    [HttpGet("Audiobooks/Popular")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogListResponse>> GetPopularAudiobooks(
        [FromQuery] AudiblePopularSort sort = AudiblePopularSort.BestSellers,
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CatalogItem> audiobooks;
        try
        {
            audiobooks = await _audible.PopularAsync(sort, limit ?? 20, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogSourceException ex)
        {
            return new CatalogListResponse { Items = [], Sources = [new CatalogSourceReport(CatalogSource.Audible, ex.Status)] };
        }

        return new CatalogListResponse
        {
            Items = await MarkOnDiskAsync(audiobooks, CatalogItemKind.Audiobook, cancellationToken).ConfigureAwait(false),
            Sources = [new CatalogSourceReport(CatalogSource.Audible, CatalogSourceStatus.Ok)]
        };
    }

    /// <summary>
    /// Gets the books trending this week, for the Books row of the Discover screen.
    /// </summary>
    /// <param name="limit">Optional. The maximum number of entries (1 to 50, default 20).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The books, most trending first, each marked on disk or missing. Empty when books are off.</response>
    /// <returns>The books.</returns>
    [HttpGet("Books/Trending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogListResponse>> GetTrendingBooks(
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CatalogItem> books;
        try
        {
            books = await _books.TrendingAsync(limit ?? 20, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogSourceException ex)
        {
            return new CatalogListResponse { Items = [], Sources = [new CatalogSourceReport(CatalogSource.Shelfmark, ex.Status)] };
        }

        return new CatalogListResponse
        {
            Items = await MarkOnDiskAsync(books, CatalogItemKind.Book, cancellationToken).ConfigureAwait(false),
            Sources = [new CatalogSourceReport(CatalogSource.Shelfmark, CatalogSourceStatus.Ok)]
        };
    }

    /// <summary>
    /// Gets the sheet of one book, marked on disk or missing.
    /// </summary>
    /// <param name="provider">The metadata provider of the book (for example <c>openlibrary</c>).</param>
    /// <param name="bookId">The id of the book in that provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The book, marked on disk or missing.</response>
    /// <response code="404">The provider does not know the book.</response>
    /// <response code="503">Books are off, or the provider cannot be reached.</response>
    /// <returns>The sheet of the book.</returns>
    [HttpGet("Books/{provider}/{bookId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CatalogItem>> GetBook(
        [FromRoute, Required, RegularExpression("^[a-z]{2,20}$")] string provider,
        [FromRoute, Required, RegularExpression("^[A-Za-z0-9_-]{1,64}$")] string bookId,
        CancellationToken cancellationToken)
    {
        CatalogItem? book;
        try
        {
            book = await _books.GetAsync(provider, bookId, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogSourceException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (book is null)
        {
            return NotFound();
        }

        return (await MarkOnDiskAsync([book], CatalogItemKind.Book, cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>
    /// Marks the entries of a list that the library already has, with the rule of the searches (the local book carrying
    /// the same ASIN, else the one with the same title and author), keeping the order and the entries of the list only.
    /// </summary>
    private async Task<IReadOnlyList<CatalogItem>> MarkOnDiskAsync(IReadOnlyList<CatalogItem> items, CatalogItemKind kind, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var userId = User.GetUserId();
        var local = new List<LocalCatalogEntry>();
        foreach (var item in items)
        {
            local.AddRange(await _index.SearchAsync(item.Title, userId, [kind], DefaultSuggestionLimit, cancellationToken).ConfigureAwait(false));
        }

        var merged = CatalogMerger.Merge(local, [], items, local.Count + items.Count)
            .GroupBy(i => i.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return items.Select(i => merged.TryGetValue(i.Key, out var marked) ? marked : i).ToList();
    }

    /// <summary>
    /// Gets the sheet of an audiobook: authors, narrators, series and position, length, cover, summary, language and market,
    /// marked on disk or missing the same way as the audiobook search.
    /// </summary>
    /// <param name="asin">The Audible ASIN (ten letters and digits).</param>
    /// <param name="market">Optional. The Audible market to read first (<c>fr</c>, <c>us</c>...), usually the one of the search result. Then the configured markets.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The sheet.</response>
    /// <response code="400">The ASIN or the market is not valid.</response>
    /// <response code="404">No configured Audible market knows this book.</response>
    /// <response code="503">Audible is not configured (no <c>AUDIBLE_MARKETS</c>) or not reachable.</response>
    /// <returns>The sheet, or the failure.</returns>
    [HttpGet("Audiobooks/{asin}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CatalogAudiobookDetails>> GetAudiobook(
        [FromRoute, Required, RegularExpression("^[A-Za-z0-9]{10}$")] string asin,
        [FromQuery] string? market,
        CancellationToken cancellationToken)
    {
        if (!AsinRegex().IsMatch(asin) || (!string.IsNullOrWhiteSpace(market) && !AudibleMarkets.TryGetBaseAddress(market.Trim(), out _)))
        {
            return BadRequest();
        }

        var userId = User.GetUserId();
        var response = await RunSourceAsync(() => _audible.GetDetailsAsync(asin, market?.Trim(), cancellationToken)).ConfigureAwait(false);
        if (response.Value is not { } details)
        {
            return response;
        }

        // The same rule as the search: the local book carrying this ASIN, else the one with this title and author.
        var local = await _index.SearchAsync(details.Title, userId, [CatalogItemKind.Audiobook], DefaultSuggestionLimit, cancellationToken).ConfigureAwait(false);
        var entry = new CatalogItem
        {
            Key = "audible:" + details.Asin,
            Kind = CatalogItemKind.Audiobook,
            Title = details.Title,
            Availability = CatalogAvailability.Missing,
            Asin = details.Asin,
            Authors = details.Authors,
            Sources = [CatalogSource.Audible]
        };
        var marked = CatalogMerger.Merge(local, [], [entry], local.Count + 1).FirstOrDefault(i => i.Key == entry.Key);
        return marked is { Availability: CatalogAvailability.OnDisk }
            ? details with { Availability = CatalogAvailability.OnDisk, LocalItemId = marked.LocalItemId }
            : details;
    }

    /// <summary>
    /// Searches books to read (ebooks, PDF, comic book archives) through the metadata provider of Shelfmark
    /// (Hardcover, Open Library or Google Books).
    /// </summary>
    /// <remarks>
    /// Each book carries the provider it comes from and its id there (<c>bookProvider</c>, <c>bookId</c>): the providers
    /// do not share their ids, and a book is requested with both.
    /// </remarks>
    /// <param name="query">The search text: a title, an author, an ISBN.</param>
    /// <param name="language">Optional. The language to keep (<c>fr</c>, <c>en</c>). A book whose provider gives no language is kept.</param>
    /// <param name="limit">Optional. The maximum number of entries, which is also the size of a page (1 to 50, default 20).</param>
    /// <param name="page">Optional. The page, from 1 (1 to 500, default 1).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The books, each marked on disk or missing, with the status of the sources. Empty, with the
    /// <c>Shelfmark</c> source <c>Disabled</c>, when Shelfmark is not configured; the books on disk only, with the source
    /// status, when Shelfmark cannot be reached or refuses the key.</response>
    /// <response code="400">No search text was given.</response>
    /// <response code="429">The user sent too many searches; retry after the Retry-After delay.</response>
    /// <returns>The books.</returns>
    [HttpGet("Books")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CatalogListResponse>> SearchBooks(
        [FromQuery, Required] string query,
        [FromQuery] string? language,
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit,
        [FromQuery, Range(1, MaxDiscoverPage)] int? page,
        CancellationToken cancellationToken)
    {
        var bookQuery = Clean(query) is { } text
            ? new BookQuery { Query = text, Language = Clean(language), Limit = limit ?? 20, Page = page ?? 1 }
            : null;
        if (bookQuery is null)
        {
            return BadRequest();
        }

        var userId = User.GetUserId();
        if (TooManyRequests(userId) is { } refused)
        {
            return refused;
        }

        IReadOnlyList<CatalogItem> books;
        CatalogSourceStatus status;
        try
        {
            books = await _books.SearchAsync(bookQuery, cancellationToken).ConfigureAwait(false);
            status = CatalogSourceStatus.Ok;
        }
        catch (CatalogSourceException ex) when (ex.Status == CatalogSourceStatus.Disabled)
        {
            // Books are off: nothing to show, not even the books on disk, and nothing is an error.
            return new CatalogListResponse { Items = [], Sources = [new CatalogSourceReport(CatalogSource.Shelfmark, CatalogSourceStatus.Disabled)] };
        }
        catch (CatalogSourceException ex)
        {
            books = [];
            status = ex.Status;
        }

        var local = await _index.SearchAsync(bookQuery.Query, userId, [CatalogItemKind.Book], bookQuery.Limit, cancellationToken).ConfigureAwait(false);
        var items = CatalogMerger.Merge(local, [], books, bookQuery.Limit);

        // The books found on disk only are listed on the first page; later pages only mark their own books on disk.
        return new CatalogListResponse
        {
            Items = bookQuery.Page == 1 ? items : items.Where(i => i.Sources.Contains(CatalogSource.Shelfmark)).ToList(),
            Sources = [new CatalogSourceReport(CatalogSource.Library, CatalogSourceStatus.Ok), new CatalogSourceReport(CatalogSource.Shelfmark, status)]
        };
    }

    /// <summary>
    /// Gets the "Recommended for you" row of the current user, built from the Letterboxd account set in their own settings, without the films already seen.
    /// </summary>
    /// <param name="limit">Optional. The maximum number of films (default 20).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The films, each marked on disk or missing, with the status of Letterboxd and TMDB.</response>
    /// <returns>The recommendations.</returns>
    [HttpGet("Recommendations/ForYou")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogListResponse>> GetRecommendedForYou(
        [FromQuery, Range(1, UniversalSearchService.MaxLimit)] int? limit,
        CancellationToken cancellationToken)
        => await _recommendations.GetForYouAsync(User.GetUserId(), limit ?? 20, cancellationToken).ConfigureAwait(false);

    private async Task<ActionResult<CatalogSearchResponse>> RunSearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();

        // Queries too short to be sent anywhere do not cost a token.
        if (UniversalSearchService.NormalizeQuery(request.Query).Length >= UniversalSearchService.MinQueryLength
            && TooManyRequests(userId) is { } refused)
        {
            return refused;
        }

        return await _search.SearchAsync(request, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the series types by name. Unlike the comma delimited binder, an unknown type is refused instead of dropped,
    /// so a typo never widens the row to every series.
    /// </summary>
    private static bool TryParseSeriesTypes(string? value, out IReadOnlyList<TmdbSeriesType> types)
    {
        var parsed = new List<TmdbSeriesType>();
        types = parsed;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // Names only: a number would bind to any value, defined or not.
            if (!char.IsLetter(name[0]) || !Enum.TryParse<TmdbSeriesType>(name, ignoreCase: true, out var type))
            {
                return false;
            }

            parsed.Add(type);
        }

        return true;
    }

    private static string? Clean(string? value)
    {
        var normalized = UniversalSearchService.NormalizeQuery(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private ObjectResult? TooManyRequests(Guid userId)
    {
        if (_rateLimiter.TryAcquire(userId, out var retryAfter))
        {
            return null;
        }

        Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return StatusCode(StatusCodes.Status429TooManyRequests, new CatalogSourceReport(CatalogSource.Library, CatalogSourceStatus.RateLimited));
    }

    [GeneratedRegex("^[A-Za-z0-9]{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex AsinRegex();

    private async Task<ActionResult<T>> RunSourceAsync<T>(Func<Task<T?>> call)
        where T : class
    {
        try
        {
            var result = await call().ConfigureAwait(false);
            return result is null ? NotFound() : result;
        }
        catch (CatalogSourceException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CatalogSourceReport(ex.CatalogSource, ex.Status));
        }
    }
}
