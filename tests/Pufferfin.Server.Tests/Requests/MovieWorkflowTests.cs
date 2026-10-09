using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Integration;
using Pufferfin.Server.Requests.Workflow;
using Pufferfin.Server.Tests.AutoConfig;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class MovieWorkflowTests : IDisposable
{
    private readonly RequestTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private void Releases(params JsonObject[] releases) => _kit.Server.Resources["release"] = new JsonArray(releases.Select(r => (JsonNode)r).ToArray());

    private Task<MediaRequest> Search(MediaRequest? request = null, RequestContext? context = null)
        => _kit.Movies.SearchAsync(request ?? RequestTestKit.Request(), context ?? RequestTestKit.Context(), CancellationToken.None);

    [Fact]
    public async Task ANewFilm_IsAddedToRadarr_ThenTheReleaseInThePreferredLanguageIsGrabbed()
    {
        Releases(RequestTestKit.ReleaseJson("Dune.2021.1080p.ENG", ["English"]), RequestTestKit.ReleaseJson("Dune.2021.MULTi.VFF.1080p", ["French", "English"]));

        var result = await Search();

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Equal("fr", result.ChosenLanguage);
        Assert.Equal(["Dune.2021.MULTi.VFF.1080p"], _kit.Grabs);
        var added = Assert.Single(_kit.Server.Resource("movie"))!.AsObject();
        Assert.Equal(7, added["qualityProfileId"]!.GetValue<int>());
        Assert.Equal("/media/movies", added["rootFolderPath"]!.GetValue<string>());
        Assert.False(added["addOptions"]!["searchForMovie"]!.GetValue<bool>());
        Assert.Equal(result.ArrId, added["id"]!.GetValue<int>());
        Assert.Equal(RequestTestKit.Now, result.GrabbedAt);
    }

    [Fact]
    public async Task AnAnime_IsFiledInTheAnimeRootFolder()
    {
        _kit.Metadata.Movies[129] = new TitleMetadata(129, "Spirited Away", "ja", [16, 14], ["JP"]);
        Releases(RequestTestKit.ReleaseJson("Spirited.Away.2001.MULTi.1080p", ["French", "Japanese"]));

        await Search(RequestTestKit.Request(tmdbId: 129));

        Assert.Equal("/media/anime-movies", _kit.Server.Resource("movie").Single()!["rootFolderPath"]!.GetValue<string>());
    }

    [Fact]
    public async Task AGrabInTheHistory_NotYetInTheQueue_IsNotDownloadedAgain()
    {
        _kit.AddMovie();
        _kit.Server.Resource("history/movie").Add(new JsonObject { ["eventType"] = "grabbed", ["date"] = RequestTestKit.Now.AddMinutes(-1), ["downloadId"] = "ABC" });
        Releases(RequestTestKit.ReleaseJson("Dune.2021.MULTi.VFF.1080p", ["French"]));

        var result = await Search();

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Empty(_kit.Grabs);
        Assert.DoesNotContain(_kit.Server.Calls, c => c.Path.StartsWith("release", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGrabByRadarrDuringTheSlowSearch_IsNotDownloadedAgain()
    {
        _kit.AddMovie();
        _kit.Server.Overrides.Insert(0, (method, path, _) =>
        {
            if (method == HttpMethod.Get && path.StartsWith("release?", StringComparison.Ordinal))
            {
                // Radarr's RSS sync grabs the film while the interactive search runs.
                _kit.Server.Resource("history/movie").Add(new JsonObject { ["eventType"] = "grabbed", ["date"] = RequestTestKit.Now, ["downloadId"] = "RSS" });
                return FakeArrServer.Json(new JsonArray(RequestTestKit.ReleaseJson("Dune.2021.MULTi.VFF.1080p", ["French"])));
            }

            return null;
        });

        var result = await Search();

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Empty(_kit.Grabs);
    }

    [Fact]
    public async Task WithoutThePreferredLanguage_TheOriginalVersionIsTaken_AndTheProfileFollows()
    {
        _kit.AddMovie();
        Releases(RequestTestKit.ReleaseJson("Dune.2021.GERMAN.1080p", ["German"]), RequestTestKit.ReleaseJson("Dune.2021.1080p.ENG", ["English"]));

        var result = await Search();

        Assert.Equal(MediaRequest.OriginalLanguage, result.ChosenLanguage);
        Assert.Equal(["Dune.2021.1080p.ENG"], _kit.Grabs);
        var update = Assert.Single(_kit.Server.Writes, w => w.Method == HttpMethod.Put);
        Assert.Equal("movie/50", update.Path);
        Assert.Equal(8, JsonNode.Parse(update.Body!)!["qualityProfileId"]!.GetValue<int>());
    }

    [Fact]
    public async Task AFilmUnmonitoredByHand_IsMonitoredAgain_WhenRequested()
    {
        _kit.AddMovie()["monitored"] = false;
        Releases(RequestTestKit.ReleaseJson("Dune.2021.MULTi.VFF.1080p", ["French"]));

        await Search();

        var update = Assert.Single(_kit.Server.Writes, w => w.Method == HttpMethod.Put);
        Assert.True(JsonNode.Parse(update.Body!)!["monitored"]!.GetValue<bool>());
        Assert.Equal(7, JsonNode.Parse(update.Body!)!["qualityProfileId"]!.GetValue<int>());
    }

    [Fact]
    public async Task WithoutPreferredNorOriginalLanguage_TheRequestFails_WithAnExplicitReason()
    {
        _kit.AddMovie();
        Releases(RequestTestKit.ReleaseJson("Dune.2021.GERMAN.1080p", ["German"]), RequestTestKit.ReleaseJson("Dune.2021.VOSTFR.1080p", ["French"]));

        var result = await Search();

        Assert.Equal(RequestState.Failed, result.State);
        Assert.Equal(RequestProblemCode.NoAcceptableLanguage, result.Problem!.Code);
        Assert.Empty(_kit.Grabs);
    }

    [Fact]
    public async Task ASubtitledOnlyRelease_IsRefused_WhenTheAudioIsWantedInThatLanguage()
    {
        _kit.AddMovie();
        Releases(RequestTestKit.ReleaseJson("Dune.2021.VOSTFR.1080p", ["French"]), RequestTestKit.ReleaseJson("Dune.2021.1080p.ENG", ["English"]));

        var result = await Search();

        Assert.Equal(["Dune.2021.1080p.ENG"], _kit.Grabs);
        Assert.Equal(MediaRequest.OriginalLanguage, result.ChosenLanguage);
    }

    [Fact]
    public async Task NoReleaseYet_IsAWaitForSources_NotAFailure()
    {
        _kit.AddMovie();

        var result = await Search();

        Assert.Equal(RequestState.WaitingForSources, result.State);
        Assert.Equal(RequestProblemCode.NoRelease, result.Problem!.Code);
        Assert.True(result.NextAttemptAt > RequestTestKit.Now);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task UnreachableIndexers_AreAWaitForSources_WithTheirReason()
    {
        _kit.AddMovie();
        _kit.Server.Resource("indexerstatus").Add(new JsonObject { ["indexerId"] = 1, ["disabledTill"] = RequestTestKit.Now.AddHours(1).ToString("o") });

        var result = await Search();

        Assert.Equal(RequestState.WaitingForSources, result.State);
        Assert.Equal(RequestProblemCode.IndexersUnavailable, result.Problem!.Code);
    }

    [Fact]
    public async Task ARadarrWithoutIndexerStatus_StillWaitsForSources()
    {
        // Radarr 6 and Sonarr 4 answer 404 to indexerstatus: no news of failing indexers, not a failure
        _kit.AddMovie();
        _kit.Server.Resources.Remove("indexerstatus");

        var result = await Search();

        Assert.Equal(RequestState.WaitingForSources, result.State);
        Assert.Equal(RequestProblemCode.NoRelease, result.Problem!.Code);
    }

    [Fact]
    public async Task NoIndexerAtAll_Fails()
    {
        _kit.AddMovie();
        _kit.Server.Resources["indexer"] = [];

        var result = await Search();

        Assert.Equal(RequestState.Failed, result.State);
        Assert.Equal(RequestProblemCode.NoIndexer, result.Problem!.Code);
    }

    [Fact]
    public async Task AnUnreachableRadarr_KeepsTheRequest_AndLeaksNoKey()
    {
        _kit.Server.Overrides.Insert(0, (_, _, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "radarr:7878 " + ArrFixtures.RadarrKey));

        var result = await Search();

        Assert.Equal(RequestState.Searching, result.State);
        Assert.Equal(RequestProblemCode.ServiceUnavailable, result.Problem!.Code);
        Assert.True(result.NextAttemptAt > RequestTestKit.Now);
        Assert.DoesNotContain(ArrFixtures.RadarrKey, result.Problem.Message + result.Problem.Detail, StringComparison.Ordinal);
        Assert.All(_kit.Logs.Entries, e => Assert.DoesNotContain(ArrFixtures.RadarrKey, e, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RadarrNotConfigured_Fails_WithTheReason()
    {
        var context = new RequestContext(ArrFixtures.Settings(), RequestTestKit.Now, []);

        var result = await Search(context: context);

        Assert.Equal(RequestProblemCode.ServiceDisabled, result.Problem!.Code);
        Assert.Empty(_kit.Server.Calls);
    }

    private static MediaRequest Downloading()
        => RequestTestKit.Request() with { State = RequestState.Downloading, ArrId = 50, GrabbedAt = RequestTestKit.Now.AddMinutes(-30), Attempts = 1 };

    [Fact]
    public async Task Tracking_ReadsTheProgressFromTheQueue()
    {
        _kit.AddMovie();
        _kit.Server.Resource("queue/details").Add(new JsonObject { ["movieId"] = 50, ["size"] = 1000d, ["sizeleft"] = 250d, ["timeleft"] = "00:02:00", ["trackedDownloadState"] = "downloading", ["trackedDownloadStatus"] = "ok" });

        var result = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Equal(0.75, result.Progress!.Fraction);
        Assert.Equal(120, result.Progress.EtaSeconds);
    }

    [Fact]
    public async Task Tracking_AFilmTheLibraryTookForAnother_IsAvailableUnderItsFolder()
    {
        // Imported by Radarr to "The Hunt (2012)", identified by the library as another "The Hunt"
        var movie = _kit.AddMovie(hasFile: true);
        movie["path"] = "/movies/The Hunt (2012)";
        _kit.Library.MovieFolders.Add("The Hunt (2012)");

        var result = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Available, result.State);
    }

    [Fact]
    public async Task Tracking_AnImportedFilmNotInTheLibraryYet_IsAddingToTheLibrary()
    {
        var movie = _kit.AddMovie(hasFile: true);
        movie["path"] = "/movies/The Hunt (2012)";

        var result = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Importing, result.State);
    }

    [Fact]
    public async Task Tracking_AStalledDownload_SaysNoSeedSharesIt()
    {
        _kit.AddMovie();
        _kit.Server.Resource("queue/details").Add(new JsonObject
        {
            ["movieId"] = 50,
            ["size"] = 1000d,
            ["sizeleft"] = 1000d,
            ["trackedDownloadState"] = "downloading",
            // As Sonarr and Radarr report it: an "ok" status, the stall in the message
            ["trackedDownloadStatus"] = "ok",
            ["errorMessage"] = "The download is stalled with no connections"
        });

        var result = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Equal(RequestProblemCode.StalledNoSeeds, result.Problem!.Code);
    }

    [Fact]
    public async Task Tracking_AnImportBlocked_FailsWithTheReasonRadarrGives()
    {
        _kit.AddMovie();
        _kit.Server.Resource("queue/details").Add(JsonNode.Parse("""
            {"movieId":50,"size":1000,"sizeleft":0,"trackedDownloadState":"importBlocked","trackedDownloadStatus":"warning",
             "statusMessages":[{"title":"Dune.2021.mkv","messages":["Not a Custom Format upgrade for existing movie file(s)"]}]}
            """)!);

        var result = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Failed, result.State);
        Assert.Equal(RequestProblemCode.ImportBlocked, result.Problem!.Code);
        Assert.Contains("Not a Custom Format upgrade", result.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tracking_AnImportBlocked_KeepsTheCredentialsOfADownloadUrlOutOfTheReason()
    {
        _kit.AddMovie();
        _kit.Server.Resource("queue/details").Add(JsonNode.Parse($$"""
            {"movieId":50,"size":1000,"sizeleft":0,"trackedDownloadState":"importBlocked","trackedDownloadStatus":"warning",
             "errorMessage":"Download failed: http://prowlarr:9696/1/download?apikey={{ArrFixtures.ProwlarrKey}}&link=abc and https://tracker.example/dl/42?passkey=0a1b2c3d4e5f"}
            """)!);
        var context = RequestTestKit.Context() with { Settings = ArrFixtures.Settings((SettingKeys.RadarrUrl, "http://radarr:7878"), (SettingKeys.RadarrApiKey, ArrFixtures.RadarrKey), (SettingKeys.ProwlarrUrl, "http://prowlarr:9696"), (SettingKeys.ProwlarrApiKey, ArrFixtures.ProwlarrKey)) };

        var result = await _kit.Movies.TrackAsync(Downloading(), context, CancellationToken.None);

        Assert.Equal(RequestProblemCode.ImportBlocked, result.Problem!.Code);
        Assert.Contains("Download failed", result.Problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(ArrFixtures.ProwlarrKey, result.Problem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0a1b2c3d4e5f", result.Problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tracking_AFailedDownload_KeepsTheKeysOutOfTheReasonFromTheHistory()
    {
        _kit.AddMovie();
        _kit.Server.Resource("history/movie").Add(new JsonObject { ["eventType"] = "grabbed", ["date"] = RequestTestKit.Now.AddMinutes(-30), ["downloadId"] = "A" });
        _kit.Server.Resource("history/movie").Add(JsonNode.Parse("{\"eventType\":\"downloadFailed\",\"date\":\"" + RequestTestKit.Now.AddMinutes(-5).ToString("o") + "\",\"downloadId\":\"A\",\"data\":{\"message\":\"Rejected with key " + ArrFixtures.RadarrKey + "\"}}")!);

        var stopped = await _kit.Movies.TrackAsync(Downloading() with { Attempts = ArrWorkflowBase.MaxSearchAttempts }, RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Failed, stopped.State);
        Assert.Equal("Rejected with key [redacted]", stopped.Problem!.Detail);
    }

    [Fact]
    public async Task Tracking_AFilmInTheLibrary_IsAvailable()
    {
        _kit.Library.Movies.Add(438631);

        var result = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Available, result.State);
    }

    [Fact]
    public async Task Tracking_AFileImportedByRadarr_IsImporting_AndTheLibraryIsAskedToScan()
    {
        var movie = _kit.AddMovie(hasFile: true);
        movie["path"] = "/media/movies/Dune (2021)";

        var result = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Importing, result.State);
        Assert.Equal(["/media/movies/Dune (2021)"], _kit.Library.Refreshed);
    }

    [Fact]
    public async Task Tracking_AFailedDownload_SearchesAgain_ThenStopsWithTheReason()
    {
        _kit.AddMovie();
        _kit.Server.Resource("history/movie").Add(new JsonObject { ["eventType"] = "grabbed", ["date"] = RequestTestKit.Now.AddMinutes(-30), ["downloadId"] = "A" });
        _kit.Server.Resource("history/movie").Add(JsonNode.Parse("{\"eventType\":\"downloadFailed\",\"date\":\"" + RequestTestKit.Now.AddMinutes(-5).ToString("o") + "\",\"downloadId\":\"A\",\"data\":{\"message\":\"Torrent is unregistered\"}}")!);

        var retried = await _kit.Movies.TrackAsync(Downloading(), RequestTestKit.Context(), CancellationToken.None);
        var stopped = await _kit.Movies.TrackAsync(Downloading() with { Attempts = ArrWorkflowBase.MaxSearchAttempts }, RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Searching, retried.State);
        Assert.Equal(RequestProblemCode.DownloadFailed, retried.Problem!.Code);
        Assert.Equal(RequestState.Failed, stopped.State);
        Assert.Equal("Torrent is unregistered", stopped.Problem!.Detail);
    }

    [Fact]
    public async Task Tracking_ARecentGrabNotVisibleYet_KeepsWaiting()
    {
        _kit.AddMovie();
        var request = Downloading() with { GrabbedAt = RequestTestKit.Now.AddMinutes(-1) };

        var result = await _kit.Movies.TrackAsync(request, RequestTestKit.Context(), CancellationToken.None);

        Assert.Same(request, result);
    }

    [Fact]
    public async Task Tracking_AWaitingRequestGrabbedByRadarr_MovesToDownloading()
    {
        _kit.AddMovie();
        _kit.Server.Resource("queue/details").Add(new JsonObject { ["movieId"] = 50, ["size"] = 1000d, ["sizeleft"] = 1000d, ["trackedDownloadState"] = "downloading" });
        var waiting = RequestTestKit.Request() with { State = RequestState.WaitingForSources, ArrId = 50, Problem = new RequestProblem(RequestProblemCode.NoRelease, "No release.") };

        var result = await _kit.Movies.TrackAsync(waiting, RequestTestKit.Context(), CancellationToken.None);

        Assert.Equal(RequestState.Downloading, result.State);
        Assert.Null(result.Problem);
    }
}
