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

public sealed class TrendingTests
{
    private static readonly Guid _userId = Guid.NewGuid();

    /// <summary>
    /// Builds a trending page whose entries have the ids <paramref name="ids"/>.
    /// </summary>
    private static string Page(int page, int totalPages, string mediaType, params int[] ids)
    {
        var titleField = mediaType == "movie" ? "title" : "name";
        var results = ids.Select(id => string.Create(
            CultureInfo.InvariantCulture,
            $$"""{ "id": {{id}}, "media_type": "{{mediaType}}", "{{titleField}}": "Title {{id}}", "poster_path": "/p{{id}}.jpg" }"""));
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""{ "page": {{page}}, "total_pages": {{totalPages}}, "results": [{{string.Join(',', results)}}] }""");
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => (request, _) => Task.FromResult(respond(request));

    private static int PageOf(HttpRequestMessage request)
        => int.Parse(HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"]!, CultureInfo.InvariantCulture);

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
    public async Task GetTrending_Movies_MarksTheTitlesOnDiskWithTheirLocalItem()
    {
        using var host = new CatalogTestHost(tmdb: Respond(request => request.RequestUri!.AbsolutePath == "/3/trending/movie/week"
            ? FakeHttpHandler.Json(Page(1, 1, "movie", 1, 2, 3))
            : FakeHttpHandler.Status(HttpStatusCode.NotFound)));
        var localId = Guid.NewGuid();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = localId, Kind = CatalogItemKind.Movie, Title = "Title 2", TmdbId = 2 });

        var items = await host.Get<TmdbCatalogService>().GetTrendingAsync(CatalogItemKind.Movie, 20, _userId, CancellationToken.None);

        Assert.Equal([1, 2, 3], items.Select(i => i.TmdbId!.Value));
        Assert.All(items, i => Assert.Equal(CatalogItemKind.Movie, i.Kind));
        Assert.Equal(CatalogAvailability.Missing, items[0].Availability);
        Assert.Null(items[0].LocalItemId);
        Assert.Equal(CatalogAvailability.OnDisk, items[1].Availability);
        Assert.Equal(localId, items[1].LocalItemId);

        var query = HttpUtility.ParseQueryString(Assert.Single(host.Tmdb.Requests).Query);
        Assert.Equal("fr-FR", query["language"]);
    }

    [Fact]
    public async Task GetTrending_Series_QueriesTheTvListAndIgnoresAMovieOnDiskWithTheSameId()
    {
        using var host = new CatalogTestHost(tmdb: Respond(request => request.RequestUri!.AbsolutePath == "/3/trending/tv/week"
            ? FakeHttpHandler.Json(Page(1, 1, "tv", 7))
            : FakeHttpHandler.Status(HttpStatusCode.NotFound)));
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Movie, Title = "Title 7", TmdbId = 7 });

        var item = Assert.Single(await host.Get<TmdbCatalogService>().GetTrendingAsync(CatalogItemKind.Series, 20, _userId, CancellationToken.None));

        Assert.Equal(CatalogItemKind.Series, item.Kind);
        Assert.Equal("Title 7", item.Title);
        Assert.Equal(CatalogAvailability.Missing, item.Availability);
    }

    [Fact]
    public async Task GetTrending_LimitAboveOnePage_ReadsTheNextPages_WithoutDuplicates()
    {
        using var host = new CatalogTestHost(tmdb: Respond(request => PageOf(request) switch
        {
            1 => FakeHttpHandler.Json(Page(1, 3, "movie", Enumerable.Range(1, 20).ToArray())),

            // Title 20 moved down to page 2 between the two fetches.
            2 => FakeHttpHandler.Json(Page(2, 3, "movie", Enumerable.Range(20, 20).ToArray())),
            _ => FakeHttpHandler.Json(Page(3, 3, "movie", Enumerable.Range(40, 20).ToArray()))
        }));

        var items = await host.Get<TmdbCatalogService>().GetTrendingAsync(CatalogItemKind.Movie, 30, _userId, CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 30), items.Select(i => i.TmdbId!.Value));
        Assert.Equal([1, 2], host.Tmdb.Requests.Select(u => int.Parse(HttpUtility.ParseQueryString(u.Query)["page"]!, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task GetTrending_LastPageReached_StopsAndReturnsWhatThereIs()
    {
        using var host = new CatalogTestHost(tmdb: Respond(_ => FakeHttpHandler.Json(Page(1, 1, "movie", 1, 2))));

        var items = await host.Get<TmdbCatalogService>().GetTrendingAsync(CatalogItemKind.Movie, 50, _userId, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Single(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Endpoint_KindNotMovieOrSeries_Returns400()
    {
        using var host = new CatalogTestHost();

        var response = await CreateController(host).GetTrending(CatalogItemKind.Person, null, CancellationToken.None);

        Assert.IsType<BadRequestResult>(response.Result);
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Endpoint_ReportsTmdbOk()
    {
        using var host = new CatalogTestHost(tmdb: Respond(_ => FakeHttpHandler.Json(Page(1, 1, "movie", 1))));

        var response = (await CreateController(host).GetTrending(CatalogItemKind.Movie, 5, CancellationToken.None)).Value;

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Ok), Assert.Single(response.Sources));
    }

    [Fact]
    public async Task Endpoint_TmdbNotConfigured_ReturnsAnEmptyRowMarkedDisabled_WithoutCallingTmdb()
    {
        using var host = new CatalogTestHost(tmdb: Respond(_ => FakeHttpHandler.Json(Page(1, 1, "movie", 1))));
        host.CatalogSettings = new() { TmdbApiKey = null };

        var response = (await CreateController(host).GetTrending(CatalogItemKind.Movie, null, CancellationToken.None)).Value;

        Assert.NotNull(response);
        Assert.Empty(response.Items);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Disabled), Assert.Single(response.Sources));
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Endpoint_TmdbDown_ReturnsAnEmptyRowWithTheSourceStatusOnly()
    {
        using var host = new CatalogTestHost(tmdb: Respond(_ => FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable)));

        var response = (await CreateController(host).GetTrending(CatalogItemKind.Series, null, CancellationToken.None)).Value;

        Assert.NotNull(response);
        Assert.Empty(response.Items);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Unavailable), Assert.Single(response.Sources));
    }
}
