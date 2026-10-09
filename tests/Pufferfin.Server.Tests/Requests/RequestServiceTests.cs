using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Tests.AutoConfig;
using Pufferfin.Server.Tests.TestHelpers;
using Pufferfin.Server.Users;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class RequestServiceTests : IDisposable
{
    private readonly StoreTestContext _context = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(RequestTestKit.Now);
    private readonly RequestTestKit.FakeMetadata _metadata = new();
    private readonly PufferfinConfigurationStore _configuration;
    private readonly PufferfinUserSettingsStore _users;
    private readonly Waker _waker = new();
    private readonly Guid _user = Guid.NewGuid();

    public RequestServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _configuration = _context.Start();
        _configuration.Update(StoreTestContext.Changes(
            (SettingKeys.RadarrUrl, "http://radarr:7878"),
            (SettingKeys.RadarrApiKey, ArrFixtures.RadarrKey),
            (SettingKeys.SonarrUrl, "http://sonarr:8989"),
            (SettingKeys.SonarrApiKey, ArrFixtures.SonarrKey),
            (SettingKeys.PreferredAudioLanguages, "fr,en")));
        _users = new PufferfinUserSettingsStore(Path.Combine(_directory, "users.json"), NullLogger<PufferfinUserSettingsStore>.Instance);
        Store = new RequestStore(Path.Combine(_directory, "requests.json"), NullLogger<RequestStore>.Instance);
        Service = new RequestService(Store, _waker, _configuration, _users, _metadata, Audiobooks, _time);
    }

    private RequestStore Store { get; }

    private RequestService Service { get; }

    private RequestTestKit.FakeAudiobooks Audiobooks { get; } = new();

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private Task<RequestCreation> Create(RequestInput input, bool isAdministrator = false, Guid? user = null)
        => Service.CreateAsync(user ?? _user, isAdministrator, input, CancellationToken.None);

    [Fact]
    public async Task ARequest_IsQueued_WithTheTitle_AndWakesTheProcessor()
    {
        var outcome = await Create(new RequestInput(RequestKind.Movie, 438631));

        Assert.Equal(RequestState.Queued, outcome.Request!.State);
        Assert.Equal("Dune", outcome.Request.Title);
        Assert.Equal("fr", outcome.Request.Language);
        Assert.Equal(1, _waker.Count);
    }

    [Fact]
    public async Task TheSameRequestTwice_ReturnsTheFirstOne()
    {
        var first = await Create(new RequestInput(RequestKind.Movie, 438631));
        var second = await Create(new RequestInput(RequestKind.Movie, 438631));

        Assert.True(second.Existing);
        Assert.Equal(first.Request!.Id, second.Request!.Id);
        Assert.Single(Store.GetAll());
    }

    [Fact]
    public async Task TheLanguageChosenForASeries_IsRemembered_PerUser()
    {
        await Create(new RequestInput(RequestKind.Episode, 1399, 1, 1, Language: "en"));
        var next = await Create(new RequestInput(RequestKind.Episode, 1399, 1, 2));
        var otherUser = await Create(new RequestInput(RequestKind.Episode, 1399, 1, 2), user: Guid.NewGuid());

        Assert.Equal("en", next.Request!.Language);
        Assert.Equal("fr", otherUser.Request!.Language);
        Assert.Equal("Show S01E02", next.Request.Title);
    }

    [Fact]
    public async Task EachUser_HasTheirOwnPreferredLanguages()
    {
        _users.Set(_user, new PufferfinUserSettings { PreferredAudioLanguages = ["de", "en"] });

        var mine = await Create(new RequestInput(RequestKind.Movie, 438631));
        var someoneElse = await Create(new RequestInput(RequestKind.Movie, 438631), user: Guid.NewGuid());

        Assert.Equal("de", mine.Request!.Language);
        Assert.Equal("fr", someoneElse.Request!.Language);
    }

    [Fact]
    public async Task AUserWhoMayNotRequest_IsRefused_ButAnAdministratorIsNot()
    {
        Store.SetPermissions(_user, new RequestPermissions { CanRequest = false });

        Assert.Equal(RequestRefusal.NotAllowed, (await Create(new RequestInput(RequestKind.Movie, 438631))).Refusal);
        Assert.NotNull((await Create(new RequestInput(RequestKind.Movie, 438631), isAdministrator: true)).Request);
    }

    [Fact]
    public async Task TheWeeklyQuota_IsCheckedOnTheServer()
    {
        Store.SetPermissions(_user, new RequestPermissions { WeeklyQuota = 2 });

        await Create(new RequestInput(RequestKind.Movie, 1));
        await Create(new RequestInput(RequestKind.Movie, 2));
        var third = await Create(new RequestInput(RequestKind.Movie, 3));
        _time.Advance(TimeSpan.FromDays(8));
        var nextWeek = await Create(new RequestInput(RequestKind.Movie, 3));

        Assert.Equal(RequestRefusal.QuotaExceeded, third.Refusal);
        Assert.NotNull(nextWeek.Request);
    }

    [Fact]
    public async Task TheWeeklyQuota_HoldsAgainstConcurrentCreations()
    {
        Store.SetPermissions(_user, new RequestPermissions { WeeklyQuota = 2 });
        _metadata.Delay = TimeSpan.FromMilliseconds(50);
        var cancellationToken = TestContext.Current.CancellationToken;

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => Create(new RequestInput(RequestKind.Movie, 500 + i)), cancellationToken)));

        Assert.Equal(2, outcomes.Count(o => o.Request is not null));
        Assert.Equal(6, outcomes.Count(o => o.Refusal == RequestRefusal.QuotaExceeded));
        Assert.Equal(2, Store.GetAll().Count);
    }

    [Fact]
    public async Task TheRateLimit_HoldsAgainstConcurrentCreations()
    {
        _metadata.Delay = TimeSpan.FromMilliseconds(50);
        var cancellationToken = TestContext.Current.CancellationToken;

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 25).Select(i => Task.Run(() => Create(new RequestInput(RequestKind.Movie, 600 + i), isAdministrator: true), cancellationToken)));

        Assert.Equal(RequestService.MaxRequestsPerMinute, Store.GetAll().Count);
        Assert.Equal(25 - RequestService.MaxRequestsPerMinute, outcomes.Count(o => o.Refusal == RequestRefusal.RateLimited));
    }

    [Fact]
    public async Task TooManyRequestsInAMinute_AreRefused()
    {
        for (var i = 0; i < RequestService.MaxRequestsPerMinute; i++)
        {
            Assert.NotNull((await Create(new RequestInput(RequestKind.Movie, 100 + i), isAdministrator: true)).Request);
        }

        Assert.Equal(RequestRefusal.RateLimited, (await Create(new RequestInput(RequestKind.Movie, 999), isAdministrator: true)).Refusal);
    }

    [Fact]
    public async Task ADisabledService_RefusesTheRequest()
    {
        _configuration.Update(StoreTestContext.Changes((SettingKeys.SonarrUrl, null)));

        var outcome = await Create(new RequestInput(RequestKind.Season, 1399, 1));

        Assert.Equal(RequestRefusal.ServiceDisabled, outcome.Refusal);
        Assert.Contains("Sonarr", outcome.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RequestKind.Movie, 0, null, null, null)]
    [InlineData(RequestKind.Movie, 1, 1, null, null)]
    [InlineData(RequestKind.Season, 1, null, null, null)]
    [InlineData(RequestKind.Episode, 1, 1, null, null)]
    [InlineData(RequestKind.Episode, 1, 1, 0, null)]
    [InlineData(RequestKind.Movie, 1, null, null, "klingon")]
    public async Task AnInvalidRequest_IsRefused(RequestKind kind, int tmdbId, int? season, int? episode, string? language)
        => Assert.Equal(RequestRefusal.Invalid, (await Create(new RequestInput(kind, tmdbId, season, episode, Language: language))).Refusal);

    [Fact]
    public async Task AnUnknownTitle_IsRefused()
    {
        _metadata.Unknown.Add(5);

        Assert.Equal(RequestRefusal.TitleUnknown, (await Create(new RequestInput(RequestKind.Movie, 5))).Refusal);
    }

    [Fact]
    public async Task ARequestOfAnotherUser_CannotBeCancelledByAUser_ButByAnAdministrator()
    {
        var request = (await Create(new RequestInput(RequestKind.Movie, 438631))).Request!;

        Assert.Equal(RequestChangeOutcome.NotFound, Service.Cancel(request.Id, Guid.NewGuid(), false).Outcome);
        Assert.Equal(RequestChangeOutcome.Done, Service.Cancel(request.Id, Guid.NewGuid(), true).Outcome);
        Assert.Equal(RequestState.Cancelled, Store.Get(request.Id)!.State);
    }

    [Fact]
    public async Task OnlyAFailedRequest_CanBeRetried()
    {
        var request = (await Create(new RequestInput(RequestKind.Movie, 438631))).Request!;
        Assert.Equal(RequestChangeOutcome.InvalidState, Service.Retry(request.Id, _user, false).Outcome);

        Store.Update(request.Id, null, r => r.Fail(RequestTestKit.Now, RequestProblemCode.NoIndexer, "No indexer.") with { Attempts = 3 });
        var retried = Service.Retry(request.Id, _user, false);

        Assert.Equal(RequestChangeOutcome.Done, retried.Outcome);
        Assert.Equal(RequestState.Queued, retried.Request!.State);
        Assert.Null(retried.Request.Problem);
        Assert.Equal(0, retried.Request.Attempts);
    }

    [Fact]
    public async Task TheRequests_SurviveARestart_WithoutTheirLiveProgress()
    {
        var request = (await Create(new RequestInput(RequestKind.Movie, 438631))).Request!;
        Store.Update(request.Id, null, r => r.To(RequestState.Downloading, RequestTestKit.Now) with { Progress = new RequestProgress(0.5, 10, 60) });

        var restarted = new RequestStore(Path.Combine(_directory, "requests.json"), NullLogger<RequestStore>.Instance);

        var reloaded = restarted.Get(request.Id)!;
        Assert.Equal(RequestState.Downloading, reloaded.State);
        Assert.Null(reloaded.Progress);
        Assert.Equal(Store.GetWebhookToken(), new RequestStore(Path.Combine(_directory, "requests.json"), NullLogger<RequestStore>.Instance).GetWebhookToken());
    }

    [Fact]
    public void AProgressOnlyChange_IsPushedButNotWritten()
    {
        var request = Store.Add(RequestTestKit.Request() with { State = RequestState.Downloading });
        var pushed = 0;
        Store.Changed += (_, _) => pushed++;
        var before = File.GetLastWriteTimeUtc(Path.Combine(_directory, "requests.json"));

        var updated = Store.Update(request.Id, request.Version, r => r with { Progress = new RequestProgress(0.3, null, null) });

        Assert.Equal(1, pushed);
        Assert.Equal(request.Version, updated!.Version);
        Assert.Equal(before, File.GetLastWriteTimeUtc(Path.Combine(_directory, "requests.json")));
    }

    [Fact]
    public async Task AnAudiobook_WithoutShelfmark_IsRefused_AsADisabledService()
    {
        var outcome = await Create(new RequestInput(RequestKind.Audiobook, 0, Asin: RequestTestKit.FakeAudiobooks.Dune.Asin));

        Assert.Equal(RequestRefusal.ServiceDisabled, outcome.Refusal);
        Assert.Empty(Store.GetAll());
    }

    [Fact]
    public async Task AnAudiobook_IsQueued_InTheLanguageOfTheBook()
    {
        EnableShelfmark();
        Audiobooks.Books["B00ENGLISH"] = RequestTestKit.FakeAudiobooks.Dune with { Asin = "B00ENGLISH", Language = "en", Market = "us" };

        var outcome = await Create(new RequestInput(RequestKind.Audiobook, 0, Asin: "b00english", Market: "US"));

        var request = outcome.Request!;
        Assert.Equal(RequestState.Queued, request.State);
        Assert.Equal("B00ENGLISH", request.Asin);
        Assert.Equal("us", request.Market);
        Assert.Equal("Dune", request.Title);

        // The language chosen when searching Audible wins over the preferred languages (fr, en).
        Assert.Equal("en", request.Language);
    }

    [Fact]
    public async Task TheSameAudiobookTwice_ReturnsTheFirstRequest_AndAnotherBookIsNotADuplicate()
    {
        EnableShelfmark();
        Audiobooks.Books["B0OTHER001"] = RequestTestKit.FakeAudiobooks.Dune with { Asin = "B0OTHER001", Title = "Le Messie de Dune" };

        var first = await Create(new RequestInput(RequestKind.Audiobook, 0, Asin: RequestTestKit.FakeAudiobooks.Dune.Asin));
        var again = await Create(new RequestInput(RequestKind.Audiobook, 0, Asin: RequestTestKit.FakeAudiobooks.Dune.Asin.ToLowerInvariant()));
        var other = await Create(new RequestInput(RequestKind.Audiobook, 0, Asin: "B0OTHER001"));

        Assert.True(again.Existing);
        Assert.Equal(first.Request!.Id, again.Request!.Id);
        Assert.False(other.Existing);
        Assert.Equal(2, Store.GetAll().Count);
    }

    [Theory]
    [InlineData(null, 0, null)]
    [InlineData("short", 0, null)]
    [InlineData("B002V1A0WE", 438631, null)]
    [InlineData("B002V1A0WE", 0, "atlantis")]
    public async Task AnInvalidAudiobook_IsRefused(string? asin, int tmdbId, string? market)
    {
        EnableShelfmark();

        var outcome = await Create(new RequestInput(RequestKind.Audiobook, tmdbId, Asin: asin, Market: market));

        Assert.Equal(RequestRefusal.Invalid, outcome.Refusal);
    }

    [Fact]
    public async Task AnUnknownAudiobook_IsRefused()
    {
        EnableShelfmark();

        var outcome = await Create(new RequestInput(RequestKind.Audiobook, 0, Asin: "B000000000"));

        Assert.Equal(RequestRefusal.TitleUnknown, outcome.Refusal);
    }

    [Fact]
    public async Task AFilm_WithAnAsin_IsRefused()
    {
        var outcome = await Create(new RequestInput(RequestKind.Movie, 438631, Asin: "B002V1A0WE"));

        Assert.Equal(RequestRefusal.Invalid, outcome.Refusal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ABookToRead_IsRefused_AsNoLongerOffered(bool shelfmark)
    {
        if (shelfmark)
        {
            EnableShelfmark();
        }

        var outcome = await Create(new RequestInput(RequestKind.Ebook, 0));

        Assert.Equal(RequestRefusal.Invalid, outcome.Refusal);
        Assert.Equal("Books to read are no longer offered.", outcome.Message);
        Assert.Empty(Store.GetAll());
    }

    [Fact]
    public void ARequestForABookToRead_MadeBeforeTheyWereRemoved_StillLoads()
    {
        // As the server wrote it when books to read were offered.
        var path = Path.Combine(_directory, "old-requests.json");
        File.WriteAllText(path, """
            {
              "Version": 1,
              "Requests": [
                {
                  "Id": "28fddd02-ed1d-41c8-826c-3c1e3347e08e",
                  "UserId": "ad82ce3c-0e1f-47db-b5e4-3627eb26d308",
                  "Kind": "Ebook",
                  "TmdbId": 0,
                  "Asin": null,
                  "Market": null,
                  "BookProvider": "openlibrary",
                  "BookId": "OL82563W",
                  "SeasonNumber": null,
                  "EpisodeNumber": null,
                  "AirDate": null,
                  "Title": "Harry Potter and the Philosopher's Stone",
                  "Language": "fr",
                  "State": "Cancelled",
                  "Problem": null,
                  "ExcludedReleases": [],
                  "Episodes": [],
                  "Position": 12,
                  "Attempts": 1,
                  "CreatedAt": "2026-10-08T03:27:54.0269855+00:00",
                  "UpdatedAt": "2026-10-08T03:30:06.5314912+00:00",
                  "Version": 4
                }
              ],
              "SeriesLanguages": {},
              "Permissions": {}
            }
            """);

        var request = Assert.Single(new RequestStore(path, NullLogger<RequestStore>.Instance).GetAll());
        var dto = Pufferfin.Server.Api.Models.MediaRequestDto.From(request, null);

        Assert.Equal(RequestKind.Ebook, request.Kind);
        Assert.Equal(RequestState.Cancelled, request.State);
        Assert.Equal("Harry Potter and the Philosopher's Stone", request.Title);
        Assert.Equal(("openlibrary", "OL82563W"), (dto.BookProvider, dto.BookId));
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_directory, "old-requests.json.invalid-*"));
    }

    private void EnableShelfmark()
        => _configuration.Update(StoreTestContext.Changes((SettingKeys.ShelfmarkUrl, "http://shelfmark:8084")));

    private sealed class Waker : IRequestWaker
    {
        public int Count { get; private set; }

        public void Wake() => Count++;
    }
}
