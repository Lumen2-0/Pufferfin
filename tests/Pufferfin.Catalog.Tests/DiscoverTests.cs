using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Pufferfin.Catalog.Api;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tests.TestSupport;
using Pufferfin.Catalog.Tmdb;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class DiscoverTests
{
    private static readonly Guid _userId = Guid.NewGuid();

    /// <summary>
    /// Builds a discover page whose entries have the ids <paramref name="ids"/>. Discover payloads carry no <c>media_type</c>.
    /// </summary>
    private static string Page(int page, int totalPages, bool isMovie, params int[] ids)
    {
        var titleField = isMovie ? "title" : "name";
        var results = ids.Select(id => string.Create(
            CultureInfo.InvariantCulture,
            $$"""{ "id": {{id}}, "{{titleField}}": "Title {{id}}", "poster_path": "/p{{id}}.jpg" }"""));
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""{ "page": {{page}}, "total_pages": {{totalPages}}, "results": [{{string.Join(',', results)}}] }""");
    }

    /// <summary>
    /// Serves numbered TMDB pages of 20 entries: page n holds the ids (n - 1) * 20 + 1 to n * 20.
    /// </summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> NumberedPages(int totalPages, bool isMovie = true)
        => (request, _) =>
        {
            var page = PageOf(request.RequestUri!);
            return Task.FromResult(FakeHttpHandler.Json(Page(page, totalPages, isMovie, Enumerable.Range(((page - 1) * 20) + 1, 20).ToArray())));
        };

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => (request, _) => Task.FromResult(respond(request));

    private static int PageOf(Uri uri)
        => int.Parse(HttpUtility.ParseQueryString(uri.Query)["page"]!, CultureInfo.InvariantCulture);

    private static CatalogController CreateController(CatalogTestHost host)
    {
        var controller = ActivatorUtilities.CreateInstance<CatalogController>(host.Services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, _userId.ToString("N"))], "Test"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task Movies_ByGenre_QueriesDiscoverByPopularity_WithAVoteFloorAndRatingCap_AndMarksTheTitlesOnDisk()
    {
        using var host = new CatalogTestHost(tmdb: Respond(request => request.RequestUri!.AbsolutePath == "/3/discover/movie"
            ? FakeHttpHandler.Json(Page(1, 1, true, 1, 2, 3))
            : FakeHttpHandler.Status(HttpStatusCode.NotFound)));
        var localId = Guid.NewGuid();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = localId, Kind = CatalogItemKind.Movie, Title = "Title 2", TmdbId = 2 });

        var items = await host.Get<TmdbCatalogService>().DiscoverAsync(CatalogItemKind.Movie, 99, [], 20, 1, _userId, CancellationToken.None);

        Assert.Equal([1, 2, 3], items.Select(i => i.TmdbId!.Value));
        Assert.All(items, i => Assert.Equal(CatalogItemKind.Movie, i.Kind));
        Assert.Equal(CatalogAvailability.Missing, items[0].Availability);
        Assert.Equal(CatalogAvailability.OnDisk, items[1].Availability);
        Assert.Equal(localId, items[1].LocalItemId);

        var query = HttpUtility.ParseQueryString(Assert.Single(host.Tmdb.Requests).Query);
        Assert.Equal("popularity.desc", query["sort_by"]);
        Assert.Equal("false", query["include_adult"]);
        Assert.Equal("50", query["vote_count.gte"]);
        Assert.Equal("US", query["certification_country"]);
        Assert.Equal("R", query["certification.lte"]);
        Assert.Equal("99", query["with_genres"]);
        Assert.Null(query["with_type"]);
        Assert.Equal("fr-FR", query["language"]);
    }

    [Fact]
    public async Task Series_ByTypes_SendsTheTmdbCodesJoinedAsAnyOf_WithAVoteFloor_AndIgnoresAMovieOnDiskWithTheSameId()
    {
        using var host = new CatalogTestHost(tmdb: Respond(request => request.RequestUri!.AbsolutePath == "/3/discover/tv"
            ? FakeHttpHandler.Json(Page(1, 1, false, 7))
            : FakeHttpHandler.Status(HttpStatusCode.NotFound)));
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Movie, Title = "Title 7", TmdbId = 7 });

        var item = Assert.Single(await host.Get<TmdbCatalogService>().DiscoverAsync(
            CatalogItemKind.Series,
            null,
            [TmdbSeriesType.TalkShow, TmdbSeriesType.Reality, TmdbSeriesType.News, TmdbSeriesType.Reality],
            20,
            1,
            _userId,
            CancellationToken.None));

        Assert.Equal(CatalogItemKind.Series, item.Kind);
        Assert.Equal("Title 7", item.Title);
        Assert.Equal(CatalogAvailability.Missing, item.Availability);

        var query = HttpUtility.ParseQueryString(Assert.Single(host.Tmdb.Requests).Query);
        Assert.Equal("1|3|5", query["with_type"]);
        Assert.Null(query["with_genres"]);
        Assert.Equal("false", query["include_adult"]);
        Assert.Equal("50", query["vote_count.gte"]);

        // TMDB has no rating filter for series: none is sent.
        Assert.Null(query["certification_country"]);
        Assert.Null(query["certification.lte"]);
    }

    [Fact]
    public async Task Paging_SecondPageOfFifteen_ReadsTheWindowAcrossTwoTmdbPages()
    {
        using var host = new CatalogTestHost(tmdb: NumberedPages(10));

        var items = await host.Get<TmdbCatalogService>().DiscoverAsync(CatalogItemKind.Movie, null, [], 15, 2, _userId, CancellationToken.None);

        Assert.Equal(Enumerable.Range(16, 15), items.Select(i => i.TmdbId!.Value));
        Assert.Equal([1, 2], host.Tmdb.Requests.Select(PageOf));
    }

    [Fact]
    public async Task Paging_PageAlignedOnTmdb_ReadsOnlyThatPage()
    {
        using var host = new CatalogTestHost(tmdb: NumberedPages(10));

        var items = await host.Get<TmdbCatalogService>().DiscoverAsync(CatalogItemKind.Movie, null, [], 20, 3, _userId, CancellationToken.None);

        Assert.Equal(Enumerable.Range(41, 20), items.Select(i => i.TmdbId!.Value));
        Assert.Equal([3], host.Tmdb.Requests.Select(PageOf));
    }

    [Fact]
    public async Task Paging_LimitAboveOnePage_ReadsTheNextPages_WithoutDuplicates()
    {
        using var host = new CatalogTestHost(tmdb: Respond(request => PageOf(request.RequestUri!) switch
        {
            1 => FakeHttpHandler.Json(Page(1, 3, true, Enumerable.Range(1, 20).ToArray())),

            // Title 20 moved down to page 2 between the two fetches.
            _ => FakeHttpHandler.Json(Page(2, 3, true, Enumerable.Range(20, 20).ToArray()))
        }));

        var items = await host.Get<TmdbCatalogService>().DiscoverAsync(CatalogItemKind.Movie, null, [], 30, 1, _userId, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 30), items.Select(i => i.TmdbId!.Value));
    }

    [Fact]
    public async Task Paging_PastTheLastTmdbPage_ReturnsWhatThereIs()
    {
        using var host = new CatalogTestHost(tmdb: NumberedPages(1));

        var items = await host.Get<TmdbCatalogService>().DiscoverAsync(CatalogItemKind.Movie, null, [], 30, 1, _userId, CancellationToken.None);

        Assert.Equal(20, items.Count);
        Assert.Single(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Paging_BeyondTheTmdbPageCap_ReturnsNothing_WithoutCallingTmdb()
    {
        using var host = new CatalogTestHost(tmdb: NumberedPages(1000));

        // Page 500 of 50 entries starts at entry 24951, on TMDB page 1248: TMDB refuses pages above 500.
        var items = await host.Get<TmdbCatalogService>().DiscoverAsync(CatalogItemKind.Movie, null, [], 50, 500, _userId, CancellationToken.None);

        Assert.Empty(items);
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Endpoint_ParsesTheSeriesTypesByName()
    {
        using var host = new CatalogTestHost(tmdb: Respond(_ => FakeHttpHandler.Json(Page(1, 1, false, 1))));

        var response = (await CreateController(host).GetDiscover(CatalogItemKind.Series, null, " reality, TalkShow,News ", 5, null, CancellationToken.None)).Value;

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Ok), Assert.Single(response.Sources));
        Assert.Equal("1|3|5", HttpUtility.ParseQueryString(Assert.Single(host.Tmdb.Requests).Query)["with_type"]);
    }

    [Theory]
    [InlineData(CatalogItemKind.Person, null)]
    [InlineData(CatalogItemKind.Audiobook, null)]
    [InlineData(CatalogItemKind.Series, "Reality,Sitcom")]
    [InlineData(CatalogItemKind.Series, "3")]
    [InlineData(CatalogItemKind.Series, "Reality,42")]
    [InlineData(CatalogItemKind.Movie, "Documentary")]
    public async Task Endpoint_InvalidKindOrSeriesTypes_Returns400_WithoutCallingTmdb(CatalogItemKind kind, string? tvTypes)
    {
        using var host = new CatalogTestHost();

        var response = await CreateController(host).GetDiscover(kind, null, tvTypes, null, null, CancellationToken.None);

        Assert.IsType<BadRequestResult>(response.Result);
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Endpoint_NoKind_Returns400()
    {
        using var host = new CatalogTestHost();

        var response = await CreateController(host).GetDiscover(null, null, null, null, null, CancellationToken.None);

        Assert.IsType<BadRequestResult>(response.Result);
    }

    [Fact]
    public async Task Endpoint_TmdbNotConfigured_ReturnsAnEmptyRowMarkedDisabled_WithoutCallingTmdb()
    {
        using var host = new CatalogTestHost(tmdb: Respond(_ => FakeHttpHandler.Json(Page(1, 1, true, 1))));
        host.CatalogSettings = new() { TmdbApiKey = null };

        var response = (await CreateController(host).GetDiscover(CatalogItemKind.Movie, 99, null, null, null, CancellationToken.None)).Value;

        Assert.NotNull(response);
        Assert.Empty(response.Items);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Disabled), Assert.Single(response.Sources));
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Endpoint_TmdbDown_ReturnsAnEmptyRowWithTheSourceStatusOnly()
    {
        using var host = new CatalogTestHost(tmdb: Respond(_ => FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable)));

        var response = (await CreateController(host).GetDiscover(CatalogItemKind.Series, 99, null, null, null, CancellationToken.None)).Value;

        Assert.NotNull(response);
        Assert.Empty(response.Items);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Unavailable), Assert.Single(response.Sources));
    }
}
