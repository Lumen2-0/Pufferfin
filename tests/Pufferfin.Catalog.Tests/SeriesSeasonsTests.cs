using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

public sealed class SeriesSeasonsTests
{
    private const int SeriesId = 1396;

    private static readonly Guid _userId = Guid.NewGuid();

    private static CatalogTestHost CreateHost()
        => new(tmdb: (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/3/tv/1396" => FakeHttpHandler.Json(Fixtures.TmdbSeriesSeasons),
            "/3/tv/1396/season/1" => FakeHttpHandler.Json(Fixtures.TmdbSeasonOne),
            _ => FakeHttpHandler.Status(HttpStatusCode.NotFound)
        }));

    private static CatalogController CreateController(CatalogTestHost host, Guid userId)
    {
        var controller = ActivatorUtilities.CreateInstance<CatalogController>(host.Services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, userId.ToString("N"))], "Test"))
            }
        };
        return controller;
    }

    private static LocalEpisodeEntry File(int season, int? episode, int? end = null, string? title = null)
        => new() { ItemId = Guid.NewGuid(), SeasonNumber = season, EpisodeNumber = episode, EpisodeNumberEnd = end, Title = title };

    [Fact]
    public async Task Season_EpisodeOnDiskAndKnownToTmdb_AppearsOnce_CarryingBoth()
    {
        using var host = CreateHost();
        var pilot = File(1, 1, title: "Pilot (local)");
        var pilotCopy = File(1, 1);
        var folder = new LocalSeasonEntry { ItemId = Guid.NewGuid(), SeasonNumber = 1, Name = "Season 1" };
        host.Library.Series[(SeriesId, _userId)] = new LocalSeriesContent
        {
            SeriesItemId = Guid.NewGuid(),
            Seasons = [folder],
            Episodes = [pilot, pilotCopy]
        };

        var season = await host.Get<TmdbCatalogService>().GetSeasonAsync(SeriesId, 1, _userId, CancellationToken.None);

        Assert.NotNull(season);
        Assert.Equal([1, 2, 3], season.Episodes.Select(e => e.EpisodeNumber));
        var episode = season.Episodes[0];
        Assert.Equal(CatalogAvailability.OnDisk, episode.Availability);
        Assert.Equal(pilot.ItemId, episode.LocalItemId);
        Assert.Equal([CatalogSource.Library, CatalogSource.Tmdb], episode.Sources);
        Assert.Equal("Pilot", episode.Title);
        Assert.Equal(new DateOnly(2008, 1, 20), episode.AirDate);
        Assert.Equal("https://image.tmdb.org/t/p/w300/e1.jpg", episode.StillUrl);
        Assert.Equal(59, episode.RuntimeMinutes);
        Assert.Equal(CatalogAvailability.OnDisk, season.Availability);
        Assert.Equal(folder.ItemId, season.LocalItemId);
    }

    [Fact]
    public void Season_FileHoldingTwoEpisodes_MarksBothOnDisk_WithoutAThirdEntry()
    {
        var file = File(1, 2, 3, "Double episode");
        var tmdb = new[] { 1, 2, 3 }.Select(n => new TmdbModels.Episode { SeasonNumber = 1, EpisodeNumber = n, Name = "E" + n }).ToList();

        var episodes = SeriesContentMerger.MergeEpisodes(1, tmdb, [file]);

        Assert.Equal([1, 2, 3], episodes.Select(e => e.EpisodeNumber));
        Assert.Equal([null, file.ItemId, file.ItemId], episodes.Select(e => e.LocalItemId));
        Assert.Equal(2, SeriesContentMerger.CountLocalEpisodes([file], 1));
    }

    [Fact]
    public async Task Season_EpisodeOnDiskUnknownToTmdb_IsKept_AndMarkedLibraryOnly()
    {
        using var host = CreateHost();
        var extra = File(1, 9, title: "Unaired pilot");
        var unnumbered = File(1, null, title: "Deleted scenes");
        var extraCopy = File(1, 9);
        host.Library.Series[(SeriesId, _userId)] = new LocalSeriesContent { SeriesItemId = Guid.NewGuid(), Episodes = [extra, unnumbered, extraCopy] };

        var season = await host.Get<TmdbCatalogService>().GetSeasonAsync(SeriesId, 1, _userId, CancellationToken.None);

        Assert.NotNull(season);
        Assert.Equal([1, 2, 3, 9, null], season.Episodes.Select(e => e.EpisodeNumber));
        Assert.All(season.Episodes.Take(3), e => Assert.Equal(CatalogAvailability.Missing, e.Availability));

        var kept = season.Episodes[3];
        Assert.Equal(CatalogAvailability.OnDisk, kept.Availability);
        Assert.Equal(extra.ItemId, kept.LocalItemId);
        Assert.Equal("Unaired pilot", kept.Title);
        Assert.Equal([CatalogSource.Library], kept.Sources);
        Assert.Equal(unnumbered.ItemId, season.Episodes[4].LocalItemId);
        Assert.Equal([CatalogSource.Library], season.Episodes[4].Sources);
    }

    [Fact]
    public async Task Season_AbsentFromDisk_HasEveryEpisodeMissing()
    {
        using var host = CreateHost();

        var season = await host.Get<TmdbCatalogService>().GetSeasonAsync(SeriesId, 1, _userId, CancellationToken.None);

        Assert.NotNull(season);
        Assert.Equal(3, season.Episodes.Count);
        Assert.All(season.Episodes, e =>
        {
            Assert.Equal(CatalogAvailability.Missing, e.Availability);
            Assert.Null(e.LocalItemId);
            Assert.Equal([CatalogSource.Tmdb], e.Sources);
        });
        Assert.Equal(CatalogAvailability.Missing, season.Availability);
        Assert.Null(season.LocalItemId);
        Assert.Equal([CatalogSource.Tmdb], season.Sources);

        // TMDB gave no title for the third episode: no empty string reaches the client.
        Assert.Null(season.Episodes[2].Title);
        Assert.Null(season.Episodes[2].Overview);
    }

    [Fact]
    public async Task Season_UnknownToTmdbButOnDisk_IsServedFromTheLibrary_UnknownToBoth_Is404()
    {
        using var host = CreateHost();
        var file = File(7, 1, title: "Bonus");
        host.Library.Series[(SeriesId, _userId)] = new LocalSeriesContent { SeriesItemId = Guid.NewGuid(), Episodes = [file] };
        var controller = CreateController(host, _userId);

        var onDisk = await controller.GetSeriesSeason(SeriesId, 7, CancellationToken.None);
        var nowhere = await controller.GetSeriesSeason(SeriesId, 8, CancellationToken.None);

        Assert.Equal([CatalogSource.Library], onDisk.Value!.Sources);
        Assert.Equal(file.ItemId, Assert.Single(onDisk.Value.Episodes).LocalItemId);
        Assert.IsType<NotFoundResult>(nowhere.Result);
    }

    [Fact]
    public async Task Seasons_MergesTheTmdbListWithTheDisk()
    {
        using var host = CreateHost();
        var seriesItem = Guid.NewGuid();
        var folder = new LocalSeasonEntry { ItemId = Guid.NewGuid(), SeasonNumber = 1, Name = "Season 1" };
        var extraFolder = new LocalSeasonEntry { ItemId = Guid.NewGuid(), SeasonNumber = 6, Name = "Season 6" };
        host.Library.Series[(SeriesId, _userId)] = new LocalSeriesContent
        {
            SeriesItemId = seriesItem,
            Seasons = [folder, extraFolder],
            Episodes = [File(1, 1), File(1, 1), File(1, 2, 3), File(6, 1)]
        };

        var result = await host.Get<TmdbCatalogService>().GetSeasonsAsync(SeriesId, _userId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(CatalogAvailability.OnDisk, result.Availability);
        Assert.Equal(seriesItem, result.LocalItemId);
        Assert.Equal([0, 1, 2, 6], result.Seasons.Select(s => s.SeasonNumber));

        var one = result.Seasons[1];
        Assert.Equal("Season 1", one.Name);
        Assert.Equal(3, one.EpisodeCount);
        Assert.Equal(3, one.LocalEpisodeCount);
        Assert.Equal(CatalogAvailability.OnDisk, one.Availability);
        Assert.Equal(folder.ItemId, one.LocalItemId);
        Assert.Equal(new DateOnly(2008, 1, 20), one.AirDate);
        Assert.Equal("https://image.tmdb.org/t/p/w500/s1.jpg", one.PosterUrl);

        var two = result.Seasons[2];
        Assert.Equal(0, two.LocalEpisodeCount);
        Assert.Equal(CatalogAvailability.Missing, two.Availability);
        Assert.Null(two.LocalItemId);

        var six = result.Seasons[3];
        Assert.Null(six.EpisodeCount);
        Assert.Equal(1, six.LocalEpisodeCount);
        Assert.Equal(extraFolder.ItemId, six.LocalItemId);
        Assert.Equal([CatalogSource.Library], six.Sources);
    }

    [Fact]
    public async Task Availability_IsComputedForTheCallingUser()
    {
        using var host = CreateHost();
        var allowed = Guid.NewGuid();
        var restricted = Guid.NewGuid();

        // The index filters by user: the restricted user does not see the episode, as parental control hides it from them.
        host.Library.Series[(SeriesId, allowed)] = new LocalSeriesContent { SeriesItemId = Guid.NewGuid(), Episodes = [File(1, 1)] };

        var seen = await CreateController(host, allowed).GetSeriesSeason(SeriesId, 1, CancellationToken.None);
        var hidden = await CreateController(host, restricted).GetSeriesSeason(SeriesId, 1, CancellationToken.None);
        var hiddenSeasons = await CreateController(host, restricted).GetSeriesSeasons(SeriesId, CancellationToken.None);

        Assert.Equal(CatalogAvailability.OnDisk, seen.Value!.Episodes[0].Availability);
        Assert.All(hidden.Value!.Episodes, e => Assert.Equal(CatalogAvailability.Missing, e.Availability));
        Assert.All(hiddenSeasons.Value!.Seasons, s => Assert.Equal(0, s.LocalEpisodeCount));
        Assert.Null(hiddenSeasons.Value.LocalItemId);
        Assert.Equal([allowed, restricted, restricted], host.Library.SeriesContentRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, CatalogSourceStatus.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, CatalogSourceStatus.RateLimited)]
    public async Task TmdbDown_BothEndpoints_Return503WithTheSourceStatusOnly(HttpStatusCode tmdbStatus, CatalogSourceStatus expected)
    {
        using var host = new CatalogTestHost(tmdb: (_, _) => Task.FromResult(FakeHttpHandler.Status(tmdbStatus)));
        host.Library.Series[(SeriesId, _userId)] = new LocalSeriesContent { SeriesItemId = Guid.NewGuid(), Episodes = [File(1, 1)] };
        var controller = CreateController(host, _userId);

        var seasons = await controller.GetSeriesSeasons(SeriesId, CancellationToken.None);
        var season = await controller.GetSeriesSeason(SeriesId, 1, CancellationToken.None);

        foreach (var result in new[] { Assert.IsType<ObjectResult>(seasons.Result), Assert.IsType<ObjectResult>(season.Result) })
        {
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
            Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, expected), result.Value);
        }
    }

    [Fact]
    public async Task TmdbNotConfigured_Returns503Disabled_WithoutCallingTmdb()
    {
        using var host = CreateHost();
        host.CatalogSettings = new();
        var controller = CreateController(host, _userId);

        var result = Assert.IsType<ObjectResult>((await controller.GetSeriesSeason(SeriesId, 1, CancellationToken.None)).Result);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Disabled), result.Value);
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task UnknownSeries_Returns404()
    {
        using var host = CreateHost();

        var response = await CreateController(host, _userId).GetSeriesSeasons(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task TmdbAnswers_AreCached_TheDiskIsReadOnEveryCall()
    {
        using var host = CreateHost();
        var service = host.Get<TmdbCatalogService>();

        await service.GetSeasonsAsync(SeriesId, _userId, CancellationToken.None);
        await service.GetSeasonAsync(SeriesId, 1, _userId, CancellationToken.None);
        var tmdbCalls = host.Tmdb.Requests.Count;

        // A download that just finished shows up without waiting for the TMDB cache to expire.
        host.Library.Series[(SeriesId, _userId)] = new LocalSeriesContent { SeriesItemId = Guid.NewGuid(), Episodes = [File(1, 1)] };
        await service.GetSeasonsAsync(SeriesId, _userId, CancellationToken.None);
        var season = await service.GetSeasonAsync(SeriesId, 1, _userId, CancellationToken.None);

        Assert.Equal(2, tmdbCalls);
        Assert.Equal(tmdbCalls, host.Tmdb.Requests.Count);
        Assert.Equal(CatalogAvailability.OnDisk, season!.Episodes[0].Availability);
    }

    [Fact]
    public async Task NoResponseOrLog_CarriesTheTmdbKey()
    {
        using var host = new CatalogTestHost(tmdb: (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/3/tv/1396" => FakeHttpHandler.Json(Fixtures.TmdbSeriesSeasons),
            _ => FakeHttpHandler.Status(HttpStatusCode.InternalServerError)
        }));
        var controller = CreateController(host, _userId);

        var seasons = await controller.GetSeriesSeasons(SeriesId, CancellationToken.None);
        var failed = await controller.GetSeriesSeason(SeriesId, 1, CancellationToken.None);

        var json = JsonSerializer.Serialize(seasons.Value) + JsonSerializer.Serialize(((ObjectResult)failed.Result!).Value);
        Assert.DoesNotContain(CatalogTestHost.TmdbKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Logs.Entries, e => e.Contains(CatalogTestHost.TmdbKey, StringComparison.Ordinal));
    }
}
