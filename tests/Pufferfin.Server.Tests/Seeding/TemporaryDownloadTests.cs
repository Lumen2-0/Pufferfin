using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.Deletion;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Tests.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// Temporary films: they leave seven days after their import, only their own download goes, and a private torrent
/// keeps seeding until its minimum before it is removed with its files.
/// </summary>
public sealed class TemporaryDownloadTests : IDisposable
{
    private const int DuneTmdbId = 438631;
    private static readonly Guid _movieItem = Guid.NewGuid();
    private static readonly Guid _user = Guid.NewGuid();

    private readonly SeedingTestKit _kit = new();
    private readonly DeletionTests.FakeLibraryContent _library = new();
    private readonly RequestStore _requests;
    private readonly TemporaryExpiryRunner _runner;

    public TemporaryDownloadTests()
    {
        _requests = new RequestStore(Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"), "requests.json"), _kit.Logs.CreateLogger<RequestStore>());
        _library.Titles[_movieItem] = new LibraryTitle(_movieItem, LibraryTitleKind.Movie, "Dune", DuneTmdbId, null, null, null, []);
        _kit.Server.Add("movie", new JsonObject { ["id"] = 50, ["title"] = "Dune", ["tmdbId"] = DuneTmdbId, ["hasFile"] = true });

        var resolver = new TitleTorrentResolver(_kit.Arr, _kit.QBittorrent, new RequestTestKit.FakeMetadata(), _kit.Links, _kit.Logs.CreateLogger<TitleTorrentResolver>());
        var deletion = new ContentDeletionService(
            _library,
            new RequestTestKit.FakeLibrary(),
            resolver,
            _kit.Arr,
            _kit.QBittorrent,
            _kit.Store,
            _requests,
            _kit.Configuration,
            _kit.Clock,
            _kit.Logs.CreateLogger<ContentDeletionService>());
        _runner = new TemporaryExpiryRunner(_requests, _library, deletion, _kit.Clock, _kit.Logs.CreateLogger<TemporaryExpiryRunner>());
    }

    private static DateTimeOffset Now => SeedingTestKit.Now;

    private bool RadarrDeletedTheFilm
        => _kit.Server.Calls.Any(c => c.Method == HttpMethod.Delete && c.Path == "movie/50?deleteFiles=true&addImportExclusion=false");

    public void Dispose()
    {
        _runner.Dispose();
        _kit.Dispose();
    }

    [Fact]
    public void Expiry_StartsAtTheImport_OnlyForATemporaryFilmWithItsOwnDownload()
    {
        var importedAt = Now;

        Assert.Equal(importedAt + TimeSpan.FromDays(7), TemporaryDownloads.ExpiryFor(Request(), importedAt));
        Assert.Null(TemporaryDownloads.ExpiryFor(Request() with { TorrentHash = null }, importedAt));
        Assert.Null(TemporaryDownloads.ExpiryFor(Request() with { Temporary = false }, importedAt));
        Assert.Null(TemporaryDownloads.ExpiryFor(Request() with { Kind = RequestKind.Season }, importedAt));
    }

    [Fact]
    public void BecomingAvailable_SetsTheExpiryOnce()
    {
        var downloading = Request() with { State = RequestState.Downloading, ExpiresAt = null };

        var available = downloading.To(RequestState.Available, Now);
        var later = (available with { State = RequestState.Importing }).To(RequestState.Available, Now.AddDays(3));

        Assert.Equal(Now.AddDays(7), available.ExpiresAt);
        Assert.Equal(Now.AddDays(7), later.ExpiresAt);
        Assert.Null((downloading with { TorrentHash = null }).To(RequestState.Available, Now).ExpiresAt);
    }

    [Fact]
    public void Decide_WaitsForTheTime_ThenRemoves_UnlessAnotherRequestWantsTheFilm()
    {
        var request = Request();
        var permanent = Request() with { Id = Guid.NewGuid(), Temporary = false };

        Assert.Equal(TemporaryDecision.NotDue, TemporaryDownloads.Decide(request, [request], Now.AddDays(6)));
        Assert.Equal(TemporaryDecision.Remove, TemporaryDownloads.Decide(request, [request], Now.AddDays(7)));
        Assert.Equal(TemporaryDecision.Keep, TemporaryDownloads.Decide(request, [request, permanent], Now.AddDays(7)));
        Assert.Equal(TemporaryDecision.Remove, TemporaryDownloads.Decide(request, [request, permanent with { State = RequestState.Cancelled }], Now.AddDays(7)));
        Assert.Equal(TemporaryDecision.None, TemporaryDownloads.Decide(request with { ExpiryOutcome = TemporaryExpiryOutcome.Removed }, [request], Now.AddDays(8)));
        Assert.Equal(TemporaryDecision.None, TemporaryDownloads.Decide(request with { Temporary = false }, [request], Now.AddDays(8)));
        Assert.Equal(TemporaryDecision.None, TemporaryDownloads.Decide(request with { TorrentHash = null }, [request], Now.AddDays(8)));
    }

    [Fact]
    public void FindDownload_TakesTheQueue_ElseAGrabMadeAfterTheRequest()
    {
        var request = Request() with { TorrentHash = null, CreatedAt = Now.AddDays(-1) };
        ArrHistoryRecord Grabbed(string id, DateTimeOffset date) => new() { EventType = "grabbed", DownloadId = id, Date = date };

        Assert.Equal("abc", TemporaryDownloads.FindDownload(request, [new ArrQueueItem { DownloadId = "ABC" }], null));
        Assert.Null(TemporaryDownloads.FindDownload(request, [new ArrQueueItem { DownloadId = "A" }, new ArrQueueItem { DownloadId = "B" }], null));
        Assert.Equal("new", TemporaryDownloads.FindDownload(request, [], [Grabbed("OLD", Now.AddDays(-5)), Grabbed("NEW", Now)]));
        Assert.Null(TemporaryDownloads.FindDownload(request, [], [Grabbed("OLD", Now.AddDays(-5))]));
    }

    [Fact]
    public void Mismatch_RefusesAnotherFilm_AnotherRadarrId_OrAForeignTorrent()
    {
        var request = Request();

        Assert.Null(TemporaryDownloads.Mismatch(request, DuneTmdbId, 50, ["own1"]));
        Assert.Null(TemporaryDownloads.Mismatch(request, DuneTmdbId, 50, []));
        Assert.NotNull(TemporaryDownloads.Mismatch(request, 1, 50, ["own1"]));
        Assert.NotNull(TemporaryDownloads.Mismatch(request, DuneTmdbId, 51, ["own1"]));
        Assert.NotNull(TemporaryDownloads.Mismatch(request, DuneTmdbId, 50, ["own1", "other"]));
    }

    [Fact]
    public async Task PublicTorrent_GoesWithTheFilm_AtExpiry()
    {
        Grab("OWN1");
        _kit.AddTorrent("own1", isPrivate: false, seededDays: 7);
        _kit.AddTorrent("unrelated", isPrivate: false, seededDays: 7);
        var request = _requests.Add(Request());
        _kit.Clock.Advance(TimeSpan.FromDays(7));

        Assert.Equal(1, await _runner.RunAsync(CancellationToken.None));

        Assert.True(RadarrDeletedTheFilm);
        Assert.Equal(new[] { "own1" }, Assert.Single(_kit.Deletions));
        Assert.Contains(_kit.Server.Calls, c => c.Path == "torrents/delete" && c.Body!.Contains("deleteFiles=true", StringComparison.Ordinal));
        Assert.Empty(_kit.Store.GetPendingReleases());
        Assert.Equal(TemporaryExpiryOutcome.Removed, _requests.Get(request.Id)!.ExpiryOutcome);
    }

    [Fact]
    public async Task PrivateTorrent_KeepsSeedingUntilItsMinimum_ThenGoesWithItsFiles()
    {
        Grab("OWN1");
        var torrent = _kit.AddTorrent("own1", isPrivate: true, seededDays: 1);
        var request = _requests.Add(Request());
        _kit.Clock.Advance(TimeSpan.FromDays(7));

        await _runner.RunAsync(CancellationToken.None);

        // The film leaves at day 7; the torrent is not touched before its minimum.
        Assert.True(RadarrDeletedTheFilm);
        Assert.Empty(_kit.Deletions);
        Assert.Equal("own1", Assert.Single(_kit.Store.GetPendingReleases()).Hash);
        Assert.Empty((await _kit.Runner.RunCycleAsync(CancellationToken.None)).Purged);

        torrent["seeding_time"] = (long)TimeSpan.FromDays(30).TotalSeconds;
        var cycle = await _kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Equal("own1", Assert.Single(cycle.Purged).Hash);
        Assert.Contains(_kit.Server.Calls, c => c.Path == "torrents/delete" && c.Body!.Contains("deleteFiles=true", StringComparison.Ordinal));
        Assert.Equal(TemporaryExpiryOutcome.Removed, _requests.Get(request.Id)!.ExpiryOutcome);
    }

    [Fact]
    public async Task NothingHappens_BeforeTheTime_AndASecondRunChangesNothing()
    {
        Grab("OWN1");
        _kit.AddTorrent("own1", isPrivate: false, seededDays: 6);
        _requests.Add(Request());
        _kit.Clock.Advance(TimeSpan.FromDays(6));

        Assert.Equal(0, await _runner.RunAsync(CancellationToken.None));
        Assert.False(RadarrDeletedTheFilm);

        _kit.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, await _runner.RunAsync(CancellationToken.None));
        Assert.Equal(0, await _runner.RunAsync(CancellationToken.None));
        Assert.Single(_kit.Deletions);
    }

    [Fact]
    public async Task AnotherTorrentHoldingTheFilm_StopsTheExpiry_BeforeAnyChange()
    {
        Grab("OWN1");
        Grab("SOMEONEELSE");
        _kit.AddTorrent("own1", isPrivate: false, seededDays: 7);
        _kit.AddTorrent("someoneelse", isPrivate: false, seededDays: 7);
        var request = _requests.Add(Request());
        _kit.Clock.Advance(TimeSpan.FromDays(7));

        await _runner.RunAsync(CancellationToken.None);

        Assert.False(RadarrDeletedTheFilm);
        Assert.Empty(_kit.Deletions);
        Assert.Empty(_library.Deleted);
        Assert.Equal(TemporaryExpiryOutcome.NeedsAttention, _requests.Get(request.Id)!.ExpiryOutcome);
    }

    [Fact]
    public async Task APermanentRequestForTheSameFilm_KeepsIt()
    {
        Grab("OWN1");
        _kit.AddTorrent("own1", isPrivate: false, seededDays: 7);
        var request = _requests.Add(Request());
        _requests.Add(Request() with { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Temporary = false, ExpiresAt = null, TorrentHash = null });
        _kit.Clock.Advance(TimeSpan.FromDays(7));

        await _runner.RunAsync(CancellationToken.None);

        Assert.False(RadarrDeletedTheFilm);
        Assert.Empty(_kit.Deletions);
        Assert.Equal(TemporaryExpiryOutcome.Kept, _requests.Get(request.Id)!.ExpiryOutcome);
    }

    [Fact]
    public async Task AFilmNoLongerInTheLibrary_DeletesNothing()
    {
        _library.Titles.Clear();
        var request = _requests.Add(Request());
        _kit.Clock.Advance(TimeSpan.FromDays(7));

        await _runner.RunAsync(CancellationToken.None);

        Assert.DoesNotContain(_kit.Server.Calls, c => c.Method == HttpMethod.Delete);
        Assert.Empty(_kit.Deletions);
        Assert.Equal(TemporaryExpiryOutcome.AlreadyGone, _requests.Get(request.Id)!.ExpiryOutcome);
    }

    private static MediaRequest Request() => new()
    {
        Id = Guid.NewGuid(),
        UserId = _user,
        Kind = RequestKind.Movie,
        TmdbId = DuneTmdbId,
        Title = "Dune",
        Language = "fr",
        State = RequestState.Available,
        Temporary = true,
        ArrId = 50,
        TorrentHash = "own1",
        ExpiresAt = Now.AddDays(7),
        CreatedAt = Now.AddDays(-2),
        UpdatedAt = Now
    };

    private void Grab(string downloadId)
        => _kit.Server.Resource("history/movie").Add(new JsonObject
        {
            ["id"] = Random.Shared.Next(1, 100000),
            ["eventType"] = "grabbed",
            ["movieId"] = 50,
            ["downloadId"] = downloadId,
            ["sourceTitle"] = "Release." + downloadId,
            ["date"] = Now.AddDays(-1)
        });
}
