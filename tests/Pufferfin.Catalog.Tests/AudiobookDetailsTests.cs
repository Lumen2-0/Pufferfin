using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Pufferfin.Catalog.Api;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tests.TestSupport;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class AudiobookDetailsTests
{
    private const string Asin = "B0FR4NC3D1";

    private const string Product = """
        {
          "product": {
            "asin": "B0FR4NC3D1",
            "title": "Dune",
            "subtitle": "Le cycle de Dune 1",
            "authors": [{ "name": "Frank Herbert" }],
            "narrators": [{ "name": "Benjamin Jungers" }],
            "series": [{ "title": "Le cycle de Dune", "sequence": "1" }],
            "language": "French",
            "release_date": "2021-09-08",
            "runtime_length_min": 1255,
            "merchandising_summary": "<p>Short summary.</p>",
            "publisher_summary": "<p>Sur <b>Arrakis</b>, la planete des sables.</p>",
            "product_images": { "500": "https://m.media-amazon.com/images/I/dune.jpg" }
          }
        }
        """;

    private static CatalogController CreateController(CatalogTestHost host)
    {
        var controller = ActivatorUtilities.CreateInstance<CatalogController>(host.Services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, Guid.NewGuid().ToString("N"))], "Test"))
            }
        };
        return controller;
    }

    private static CatalogTestHost Host()
        => new(audible: (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/catalog/products/" + Asin, StringComparison.Ordinal)
            ? FakeHttpHandler.Json(Product)
            : FakeHttpHandler.Status(HttpStatusCode.NotFound)));

    [Fact]
    public async Task TheSheet_CarriesTheAudibleMetadata_AndIsMissingWhenNotOnDisk()
    {
        using var host = Host();

        var details = (await CreateController(host).GetAudiobook(Asin, null, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(Asin, details.Asin);
        Assert.Equal("Dune", details.Title);
        Assert.Equal("Le cycle de Dune 1", details.Subtitle);
        Assert.Equal(["Frank Herbert"], details.Authors);
        Assert.Equal(["Benjamin Jungers"], details.Narrators);
        Assert.Equal("Le cycle de Dune", details.SeriesName);
        Assert.Equal("1", details.SeriesPosition);
        Assert.Equal(1255, details.RuntimeMinutes);
        Assert.Equal("https://m.media-amazon.com/images/I/dune.jpg", details.ImageUrl);

        // The full summary of the publisher, without HTML, rather than the short one.
        Assert.Equal("Sur Arrakis , la planete des sables.", details.Overview);
        Assert.Equal("french", details.Language);
        Assert.Equal("fr", details.LanguageCode);
        Assert.Equal("fr", details.Market);
        Assert.Equal(new DateOnly(2021, 9, 8), details.ReleaseDate);
        Assert.Equal(2021, details.Year);
        Assert.Equal(CatalogAvailability.Missing, details.Availability);
        Assert.Null(details.LocalItemId);
    }

    [Fact]
    public async Task ABookOnDisk_ByItsAsin_IsMarkedOnDisk()
    {
        using var host = Host();
        var itemId = Guid.NewGuid();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = itemId, Kind = CatalogItemKind.Audiobook, Title = "Dune", Asin = Asin });

        var details = (await CreateController(host).GetAudiobook(Asin, null, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(CatalogAvailability.OnDisk, details.Availability);
        Assert.Equal(itemId, details.LocalItemId);
    }

    [Fact]
    public async Task ABookOnDisk_WithoutAsin_IsMatchedByTitleAndAuthor_LikeTheSearch()
    {
        using var host = Host();
        var itemId = Guid.NewGuid();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Audiobook, Title = "Dune", Authors = ["Brian Herbert"] });
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = itemId, Kind = CatalogItemKind.Audiobook, Title = "Dune", Authors = ["Frank Herbert"] });

        var details = (await CreateController(host).GetAudiobook(Asin, null, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(itemId, details.LocalItemId);
    }

    [Fact]
    public async Task AnotherBookOnDisk_DoesNotCount()
    {
        using var host = Host();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Audiobook, Title = "Dune", Asin = "B0OTHER001" });
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Audiobook, Title = "Dune", Authors = ["Brian Herbert"] });

        var details = (await CreateController(host).GetAudiobook(Asin, null, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(CatalogAvailability.Missing, details.Availability);
    }

    [Fact]
    public async Task TheMarketOfTheSearchResult_IsReadFirst_ThenTheConfiguredOnes()
    {
        using var host = new CatalogTestHost(audible: (request, _) => Task.FromResult(request.RequestUri!.Host == "api.audible.com"
            ? FakeHttpHandler.Json(Product)
            : FakeHttpHandler.Status(HttpStatusCode.NotFound)));
        host.CatalogSettings = new() { AudibleMarkets = ["fr", "us"] };

        var details = (await CreateController(host).GetAudiobook(Asin.ToLowerInvariant(), "uk", TestContext.Current.CancellationToken)).Value!;

        Assert.Equal("us", details.Market);
        Assert.Equal(["api.audible.co.uk", "api.audible.fr", "api.audible.com"], host.Audible.Requests.Select(r => r.Host));
    }

    [Fact]
    public async Task AnUnknownBook_Is404()
    {
        using var host = Host();

        var response = await CreateController(host).GetAudiobook("B000000000", null, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, CatalogSourceStatus.Unavailable)]
    public async Task AudibleDown_Is503_WithTheSourceStatusOnly(HttpStatusCode code, CatalogSourceStatus status)
    {
        using var host = new CatalogTestHost(audible: (_, _) => Task.FromResult(FakeHttpHandler.Status(code)));

        var response = await CreateController(host).GetAudiobook(Asin, null, TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Audible, status), result.Value);
    }

    [Fact]
    public async Task NoAudibleMarket_Is503Disabled_WithoutCallingAudible()
    {
        using var host = Host();
        host.CatalogSettings = new() { AudibleMarkets = [] };

        var response = await CreateController(host).GetAudiobook(Asin, null, TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Audible, CatalogSourceStatus.Disabled), result.Value);
        Assert.Empty(host.Audible.Requests);
    }

    [Theory]
    [InlineData("B0FR4", null)]
    [InlineData("B0FR4NC3D!", null)]
    [InlineData(Asin, "atlantis")]
    public async Task AnInvalidAsinOrMarket_Is400_WithoutCallingAudible(string asin, string? market)
    {
        using var host = Host();

        var response = await CreateController(host).GetAudiobook(asin, market, TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestResult>(response.Result);
        Assert.Empty(host.Audible.Requests);
    }

    [Fact]
    public async Task TheSheet_SerializesWithoutAnyUrlButTheCover()
    {
        using var host = Host();

        var details = (await CreateController(host).GetAudiobook(Asin, null, TestContext.Current.CancellationToken)).Value!;
        var json = JsonSerializer.Serialize(details);

        Assert.DoesNotContain("api.audible", json, StringComparison.Ordinal);
        Assert.Single(json.Split("https://"), part => part.StartsWith("m.media-amazon.com", StringComparison.Ordinal));
    }
}
