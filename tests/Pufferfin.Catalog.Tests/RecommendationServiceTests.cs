using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Letterboxd;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Recommendations;
using Pufferfin.Catalog.Tests.TestSupport;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class RecommendationServiceTests
{
    private static readonly Guid _userId = Guid.NewGuid();

    // Heat (949) and Collateral (1538) are the seeds; both recommend 111, only Heat recommends 222.
    // 1538 is recommended by Heat but already in the diary; 333 was played in Pufferfin.
    private static CatalogTestHost CreateHost()
        => new(
            tmdb: (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/3/movie/949/recommendations" => FakeHttpHandler.Json("""{"results":[{"id":222,"title":"Thief"},{"id":1538,"title":"Collateral"},{"id":111,"title":"Ronin"},{"id":333,"title":"Seen"}]}"""),
                "/3/movie/1538/recommendations" => FakeHttpHandler.Json("""{"results":[{"id":111,"title":"Ronin"}]}"""),
                _ => FakeHttpHandler.Status(HttpStatusCode.NotFound)
            }),
            letterboxd: (_, _) => Task.FromResult(FakeHttpHandler.Json(Fixtures.LetterboxdDiary)));

    [Fact]
    public async Task ForYou_RanksBySeedAgreement_AndExcludesEverythingAlreadySeen()
    {
        using var host = CreateHost();
        host.Library.PlayedMovies.Add(333);

        var response = await host.Get<RecommendationService>().GetForYouAsync(_userId, 20, CancellationToken.None);

        Assert.Equal([111, 222], response.Items.Select(i => i.TmdbId!.Value));
        Assert.All(response.Sources, s => Assert.Equal(CatalogSourceStatus.Ok, s.Status));

        // The film rated 1 star is not used as a seed.
        Assert.DoesNotContain(host.Tmdb.Requests, uri => uri.AbsolutePath.Contains("9999", StringComparison.Ordinal));
        Assert.Contains("/cinephile/rss/", Assert.Single(host.Letterboxd.Requests).AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForYou_UserWithoutLetterboxdAccount_IsDisabled()
    {
        using var host = CreateHost();
        host.LetterboxdUsername = null;

        var response = await host.Get<RecommendationService>().GetForYouAsync(_userId, 20, CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Letterboxd, CatalogSourceStatus.Disabled), Assert.Single(response.Sources));
        Assert.Empty(host.Letterboxd.Requests);
    }

    [Fact]
    public async Task ForYou_UnknownMember_ReportsLetterboxdUnavailable()
    {
        using var host = new CatalogTestHost(letterboxd: (_, _) => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.NotFound)));

        var response = await host.Get<RecommendationService>().GetForYouAsync(_userId, 20, CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(CatalogSourceStatus.Unavailable, Assert.Single(response.Sources).Status);
    }

    [Fact]
    public void ParseFeed_KeepsFilmsWithTheirTmdbIdRatingAndDate()
    {
        var entries = LetterboxdClient.ParseFeed(Fixtures.LetterboxdDiary);

        Assert.Equal([949, 1538, 9999], entries.Select(e => e.TmdbId));
        Assert.Equal(5.0, entries[0].Rating);
        Assert.Equal(new DateOnly(2026, 9, 30), entries[0].WatchedDate);
        Assert.Null(entries[2].WatchedDate);
    }
}
