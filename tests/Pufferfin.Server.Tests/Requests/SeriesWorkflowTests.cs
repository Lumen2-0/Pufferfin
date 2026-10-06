using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Integration;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class SeriesWorkflowTests : IDisposable
{
    private const int SeriesTmdbId = 1399;
    private const int SeriesId = 60;

    private readonly RequestTestKit _kit = new();

    public SeriesWorkflowTests()
    {
        _kit.Server.Resources["episode"] = (JsonArray)JsonNode.Parse("""
            [
              {"id":101,"seriesId":60,"seasonNumber":1,"episodeNumber":1,"airDate":"2024-01-10","airDateUtc":"2024-01-10T21:00:00Z","hasFile":false},
              {"id":102,"seriesId":60,"seasonNumber":1,"episodeNumber":2,"airDate":"2024-01-17","airDateUtc":"2024-01-17T21:00:00Z","hasFile":false},
              {"id":103,"seriesId":60,"seasonNumber":1,"episodeNumber":3,"airDate":"2024-01-24","airDateUtc":"2024-01-24T21:00:00Z","hasFile":false},
              {"id":201,"seriesId":60,"seasonNumber":2,"episodeNumber":1,"airDate":"2025-03-02","airDateUtc":"2025-03-02T21:00:00Z","hasFile":false}
            ]
            """)!;
    }

    public void Dispose() => _kit.Dispose();

    private void AddSeries()
        => _kit.Server.Add("series", (JsonObject)JsonNode.Parse("""
            {"id":60,"title":"Show","tvdbId":121361,"monitored":false,"qualityProfileId":7,
             "seasons":[{"seasonNumber":1,"monitored":false},{"seasonNumber":2,"monitored":false}]}
            """)!);

    private void Releases(params JsonObject[] releases) => _kit.Server.Resources["release"] = new JsonArray(releases.Select(r => (JsonNode)r).ToArray());

    private Task<MediaRequest> Search(MediaRequest request)
        => _kit.Series.SearchAsync(request, RequestTestKit.Context(), CancellationToken.None);

    private static MediaRequest Episode(int season, int episode, DateOnly? airDate = null)
        => RequestTestKit.Request(RequestKind.Episode, SeriesTmdbId, season: season, episode: episode, airDate: airDate);

    private static MediaRequest Season(int season) => RequestTestKit.Request(RequestKind.Season, SeriesTmdbId, season: season);

    private JsonNode? MonitoredEpisodes()
        => _kit.Server.Writes.Where(w => w.Path == "episode/monitor").Select(w => JsonNode.Parse(w.Body!)!["episodeIds"]).LastOrDefault();

    [Fact]
    public async Task ANewSeries_IsAddedToSonarrWithItsTvdbId()
    {
        Releases(RequestTestKit.ReleaseJson("Show.S01E02.MULTi.1080p", ["French"], season: 1, episodes: [2]));

        await Search(Episode(1, 2));

        var added = Assert.Single(_kit.Server.Resource("series"))!.AsObject();
        Assert.Equal(121361, added["tvdbId"]!.GetValue<int>());
        Assert.Equal("standard", added["seriesType"]!.GetValue<string>());
        Assert.Equal("none", added["addOptions"]!["monitor"]!.GetValue<string>());
        Assert.False(added["addOptions"]!["searchForMissingEpisodes"]!.GetValue<bool>());
    }

    [Fact]
    public async Task OneEpisode_IsMonitoredAlone_AndOnlyItsOwnReleaseIsGrabbed_NeverThePack()
    {
        AddSeries();
        Releases(
            RequestTestKit.ReleaseJson("Show.S01.MULTi.1080p", ["French"], fullSeason: true, season: 1),
            RequestTestKit.ReleaseJson("Show.S01E02.MULTi.1080p", ["French"], season: 1, episodes: [2]));

        var result = await Search(Episode(1, 2));

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Equal(["Show.S01E02.MULTi.1080p"], _kit.Grabs);
        Assert.Equal([102], MonitoredEpisodes()!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal([new RequestEpisode(102, 1, 2)], result.Episodes);
        Assert.Empty(_kit.Commands);

        // The series is monitored, its seasons are not: Sonarr must not grab the other episodes.
        var series = _kit.Server.Resource("series").Single()!;
        Assert.True(series["monitored"]!.GetValue<bool>());
        Assert.All(series["seasons"]!.AsArray(), s => Assert.False(s!["monitored"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task OneEpisode_WithOnlySeasonPacks_GrabsNothing_AndSaysWhy()
    {
        AddSeries();
        Releases(
            RequestTestKit.ReleaseJson("Show.S01.MULTi.1080p", ["French"], fullSeason: true, season: 1),
            RequestTestKit.ReleaseJson("Show.S01.1080p", ["English"], fullSeason: true, season: 1));

        var result = await Search(Episode(1, 2));

        Assert.Equal(RequestState.Failed, result.State);
        Assert.Equal(RequestProblemCode.OnlySeasonPacks, result.Problem!.Code);
        Assert.Empty(_kit.Grabs);
        Assert.Empty(_kit.Commands);
    }

    [Fact]
    public async Task OneEpisode_NumberedDifferentlyOnTmdb_IsMatchedByAirDate()
    {
        AddSeries();
        Releases(RequestTestKit.ReleaseJson("Show.S02E01.MULTi.1080p", ["French"], season: 2, episodes: [1]));

        // TMDB numbers it S03E01 and dates it a day later, time zones aside.
        var result = await Search(Episode(3, 1, new DateOnly(2025, 3, 3)));

        Assert.Equal([new RequestEpisode(201, 2, 1)], result.Episodes);
        Assert.Equal(["Show.S02E01.MULTi.1080p"], _kit.Grabs);
    }

    [Fact]
    public async Task OneEpisode_WithNeitherNumberNorDate_FailsWithTheReason()
    {
        AddSeries();

        var result = await Search(Episode(5, 9, new DateOnly(2030, 1, 1)));

        Assert.Equal(RequestProblemCode.EpisodeNotFound, result.Problem!.Code);
        Assert.Contains("S05E09", result.Problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneEpisode_AlreadyGrabbed_IsNotDownloadedAgain()
    {
        AddSeries();
        _kit.Server.Resource("history/series").Add(new JsonObject { ["episodeId"] = 102, ["eventType"] = "grabbed", ["date"] = RequestTestKit.Now.AddMinutes(-2), ["downloadId"] = "H" });
        Releases(RequestTestKit.ReleaseJson("Show.S01E02.MULTi.1080p", ["French"], season: 1, episodes: [2]));

        var result = await Search(Episode(1, 2));

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Empty(_kit.Grabs);
    }

    [Fact]
    public async Task ASeason_IsMonitored_ThenAPackIsGrabbed()
    {
        AddSeries();
        Releases(
            RequestTestKit.ReleaseJson("Show.S01E01.MULTi.1080p", ["French"], season: 1, episodes: [1]),
            RequestTestKit.ReleaseJson("Show.S01.MULTi.1080p", ["French"], fullSeason: true, season: 1));

        var result = await Search(Season(1));

        Assert.Equal(["Show.S01.MULTi.1080p"], _kit.Grabs);
        Assert.Equal([101, 102, 103], MonitoredEpisodes()!.AsArray().Select(n => n!.GetValue<int>()));
        var seasons = _kit.Server.Resource("series").Single()!["seasons"]!.AsArray();
        Assert.True(seasons.Single(s => s!["seasonNumber"]!.GetValue<int>() == 1)!["monitored"]!.GetValue<bool>());
        Assert.False(seasons.Single(s => s!["seasonNumber"]!.GetValue<int>() == 2)!["monitored"]!.GetValue<bool>());
        Assert.Equal(3, result.Episodes.Count);
    }

    [Fact]
    public async Task ASeasonPartlyThere_SearchesOnlyTheMissingEpisodes_NeverAPack()
    {
        AddSeries();
        var episodes = _kit.Server.Resource("episode");
        episodes.Single(e => e!["id"]!.GetValue<int>() == 101)!["hasFile"] = true;
        _kit.Server.Resource("queue/details").Add(new JsonObject { ["episodeId"] = 102, ["trackedDownloadState"] = "downloading", ["downloadId"] = "Q" });
        Releases(RequestTestKit.ReleaseJson("Show.S01.MULTi.1080p", ["French"], fullSeason: true, season: 1));

        var result = await Search(Season(1));

        Assert.Empty(_kit.Grabs);
        var command = Assert.Single(_kit.Commands);
        Assert.Equal("EpisodeSearch", command["name"]!.GetValue<string>());
        Assert.Equal([103], command["episodeIds"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(RequestState.Downloading, result.State);
    }

    [Fact]
    public async Task ASeasonWithoutPack_LetsSonarrTakeTheEpisodesInTheFirstLanguageThatHasThem()
    {
        AddSeries();
        Releases(
            RequestTestKit.ReleaseJson("Show.S01E01.1080p", ["English"], season: 1, episodes: [1]),
            RequestTestKit.ReleaseJson("Show.S01E02.1080p", ["English"], season: 1, episodes: [2]));

        var result = await Search(Season(1));

        Assert.Empty(_kit.Grabs);
        Assert.Equal([101, 102, 103], Assert.Single(_kit.Commands)["episodeIds"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(MediaRequest.OriginalLanguage, result.ChosenLanguage);

        // The series moves to the profile of that language, since it had a Pufferfin profile.
        var update = _kit.Server.Writes.Last(w => w.Method == HttpMethod.Put && w.Path == "series/60");
        Assert.Equal(8, JsonNode.Parse(update.Body!)!["qualityProfileId"]!.GetValue<int>());
    }

    [Fact]
    public async Task ASeriesWithoutTvdbId_FailsWithTheReason()
    {
        _kit.Metadata.Series[777] = new TitleMetadata(777, "Web Show", "en", [], ["US"]);

        var result = await Search(RequestTestKit.Request(RequestKind.Episode, 777, season: 1, episode: 1));

        Assert.Equal(RequestProblemCode.TvdbIdMissing, result.Problem!.Code);
        Assert.Empty(_kit.Server.Calls);
    }

    [Fact]
    public async Task Tracking_EpisodesInTheLibrary_AreAvailable()
    {
        _kit.Library.Episodes.Add((1, 2));
        var request = Episode(1, 2) with { State = RequestState.Downloading, ArrId = SeriesId, Episodes = [new RequestEpisode(102, 1, 2)] };

        var result = await _kit.Series.TrackAsync(request, RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Available, result.State);
    }
}
