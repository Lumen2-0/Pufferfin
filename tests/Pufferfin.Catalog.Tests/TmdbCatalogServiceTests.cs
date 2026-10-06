using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tests.TestSupport;
using Pufferfin.Catalog.Tmdb;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class TmdbCatalogServiceTests
{
    private static readonly Guid _userId = Guid.NewGuid();

    private static CatalogTestHost CreateHost()
        => new(tmdb: (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/3/movie/550" => FakeHttpHandler.Json(Fixtures.TmdbMovieWithoutLocalizedOverview),
            _ => FakeHttpHandler.Status(HttpStatusCode.NotFound)
        }));

    [Fact]
    public async Task GetMovie_NoOverviewInThePreferredLanguage_FallsBackToTheOriginalVersion()
    {
        using var host = CreateHost();
        host.CatalogSettings = new() { TmdbApiKey = CatalogTestHost.TmdbKey, TmdbLanguages = ["fr-FR"] };

        var movie = await host.Get<TmdbCatalogService>().GetMovieAsync(550, _userId, CancellationToken.None);

        Assert.NotNull(movie);
        Assert.Equal("Fight Club", movie.Title);
        Assert.Equal("A ticking-time-bomb insomniac.", movie.Overview);
        Assert.Equal("en", movie.OverviewLanguage);
        Assert.Equal("Mischief. Mayhem. Soap.", movie.Tagline);
    }

    [Fact]
    public async Task GetMovie_MapsCreditsTrailersGenresCountriesAndRecommendations()
    {
        using var host = CreateHost();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Movie, Title = "Pulp Fiction", TmdbId = 680 });

        var movie = await host.Get<TmdbCatalogService>().GetMovieAsync(550, _userId, CancellationToken.None);

        Assert.NotNull(movie);
        Assert.Equal(CatalogAvailability.Missing, movie.Availability);
        Assert.Equal(1999, movie.Year);
        Assert.Equal(139, movie.RuntimeMinutes);
        Assert.Equal("https://image.tmdb.org/t/p/w500/fc.jpg", movie.PosterUrl);
        Assert.Equal("Edward Norton", Assert.Single(movie.Cast).Name);
        Assert.Equal("David Fincher", Assert.Single(movie.Crew).Name);
        Assert.Equal(new CatalogGenre(18, "Drama"), Assert.Single(movie.Genres));
        Assert.Equal(new CatalogCountry("US", "United States of America"), Assert.Single(movie.Countries));

        // French first because it is the preferred language, featurettes dropped.
        Assert.Equal(["trailer-fr", "teaser-en"], movie.Trailers.Select(t => t.Key));
        Assert.Equal("https://www.youtube.com/watch?v=trailer-fr", movie.Trailers[0].Url);

        var recommendation = Assert.Single(movie.Recommendations);
        Assert.Equal(CatalogAvailability.OnDisk, recommendation.Availability);
        Assert.Equal(CatalogItemKind.Movie, recommendation.Kind);
    }

    [Fact]
    public async Task GetMovie_OnDisk_IsMarkedWithTheLocalItem()
    {
        using var host = CreateHost();
        var localId = Guid.NewGuid();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = localId, Kind = CatalogItemKind.Movie, Title = "Fight Club", TmdbId = 550 });

        var movie = await host.Get<TmdbCatalogService>().GetMovieAsync(550, _userId, CancellationToken.None);

        Assert.Equal(CatalogAvailability.OnDisk, movie!.Availability);
        Assert.Equal(localId, movie.LocalItemId);
    }

    [Fact]
    public async Task GetMovie_UnknownId_ReturnsNull()
    {
        using var host = CreateHost();

        Assert.Null(await host.Get<TmdbCatalogService>().GetMovieAsync(1, _userId, CancellationToken.None));
    }

    [Fact]
    public async Task GetMovie_Disabled_ThrowsWithoutCallingTmdb()
    {
        using var host = CreateHost();
        host.CatalogSettings = new();

        var ex = await Assert.ThrowsAsync<CatalogSourceException>(() => host.Get<TmdbCatalogService>().GetMovieAsync(550, _userId, CancellationToken.None));

        Assert.Equal(CatalogSourceStatus.Disabled, ex.Status);
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task ReadAccessToken_IsSentAsBearerHeader_NotInTheUrl()
    {
        using var host = CreateHost();
        const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJ0ZXN0In0.c2lnbmF0dXJl";
        host.CatalogSettings = new() { TmdbApiKey = Token };

        await host.Get<TmdbCatalogService>().GetMovieAsync(550, _userId, CancellationToken.None);

        Assert.Equal("Bearer " + Token, Assert.Single(host.Tmdb.AuthorizationHeaders));
        Assert.DoesNotContain(host.Tmdb.Requests, uri => uri.Query.Contains("api_key", StringComparison.Ordinal));
    }

    [Fact]
    public void Localization_PrefersTheRegionalVariant_ThenTheLanguage()
    {
        var translations = new TmdbModels.TranslationList
        {
            Translations =
            [
                new() { Iso6391 = "fr", Iso31661 = "CA", Data = new() { Title = "Title fr-CA" } },
                new() { Iso6391 = "fr", Iso31661 = "FR", Data = new() { Title = "Title fr-FR" } }
            ]
        };

        Assert.Equal("Title fr-FR", TmdbLocalization.Pick(translations, ["fr-FR"], "en", d => d.Title, "Original", null).Value);
        Assert.Equal("Title fr-CA", TmdbLocalization.Pick(translations, ["fr-CA"], "en", d => d.Title, "Original", null).Value);
        Assert.Equal("Original", TmdbLocalization.Pick(translations, ["de-DE"], "en", d => d.Title, "Original", null).Value);
    }
}
