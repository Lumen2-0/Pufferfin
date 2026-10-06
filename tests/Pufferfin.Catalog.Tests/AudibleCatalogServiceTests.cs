using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tests.TestSupport;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class AudibleCatalogServiceTests
{
    [Fact]
    public async Task Search_FiltersOnTheLanguageChosenAtSearchTime()
    {
        using var host = new CatalogTestHost(audible: (_, _) => Task.FromResult(FakeHttpHandler.Json(Fixtures.AudibleSearchDune)));

        var french = await host.Get<AudibleCatalogService>().SearchAsync(new AudibleQuery { Title = "Dune", Language = "fr-FR" }, CancellationToken.None);
        var all = await host.Get<AudibleCatalogService>().SearchAsync(new AudibleQuery { Title = "Dune" }, CancellationToken.None);

        var book = Assert.Single(french);
        Assert.Equal("B0FR4NC3D1", book.Asin);
        Assert.Equal(["Frank Herbert"], book.Authors);
        Assert.Equal("Dune, 1", book.SeriesName);
        Assert.Equal("On Arrakis, the desert planet.", book.Overview);
        Assert.Equal("fr", book.Market);
        Assert.Equal(2, all.Count);
        Assert.Contains("title=dune", Assert.Single(host.Audible.Requests).Query, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_SeveralMarkets_DeduplicatesByAsin_AndSurvivesAFailingMarket()
    {
        using var host = new CatalogTestHost(audible: (request, _) => Task.FromResult(request.RequestUri!.Host switch
        {
            "api.audible.de" => FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable),
            _ => FakeHttpHandler.Json(Fixtures.AudibleSearchDune)
        }));
        host.Settings = new() { AudibleMarkets = ["fr", "de", "us", "xx"] };

        var books = await host.Get<AudibleCatalogService>().SearchAsync(new AudibleQuery { Author = "Frank Herbert" }, CancellationToken.None);

        Assert.Equal(["B0FR4NC3D1", "B0EN6L1SH2"], books.Select(b => b.Asin));
        Assert.DoesNotContain(host.Audible.Requests, uri => uri.Host == "api.audible.xx");
    }

    [Fact]
    public async Task Search_AllMarketsFailing_Throws()
    {
        using var host = new CatalogTestHost(audible: (_, _) => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.BadGateway)));

        var ex = await Assert.ThrowsAsync<CatalogSourceException>(() => host.Get<AudibleCatalogService>().SearchAsync(new AudibleQuery { Keywords = "dune" }, CancellationToken.None));

        Assert.Equal(CatalogSourceStatus.Unavailable, ex.Status);
    }

    [Fact]
    public async Task Search_SeriesCriterion_KeepsOnlyBooksOfThatSeries()
    {
        using var host = new CatalogTestHost(audible: (_, _) => Task.FromResult(FakeHttpHandler.Json(Fixtures.AudibleSearchDune)));

        var books = await host.Get<AudibleCatalogService>().SearchAsync(new AudibleQuery { Series = "dune" }, CancellationToken.None);

        Assert.Equal("B0FR4NC3D1", Assert.Single(books).Asin);
        Assert.Contains("keywords=dune", Assert.Single(host.Audible.Requests).Query, System.StringComparison.Ordinal);
    }
}
