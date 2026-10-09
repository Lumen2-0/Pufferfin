using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Workflow;
using Pufferfin.Server.Tests.AutoConfig;
using Pufferfin.Server.Tests.TestHelpers;
using Pufferfin.Server.Users;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class ProcessorTests : IDisposable
{
    private readonly StoreTestContext _context = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(RequestTestKit.Now);
    private readonly RequestStore _store;
    private readonly PufferfinConfigurationStore _configuration;
    private readonly FakeWorkflow _workflow = new();
    private readonly RequestProcessor _processor;
    private readonly RequestService _service;
    private readonly ConcurrentBag<int> _activeSnapshots = [];

    public ProcessorTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new RequestStore(Path.Combine(_directory, "requests.json"), NullLogger<RequestStore>.Instance);
        _configuration = _context.Start();
        _configuration.Update(StoreTestContext.Changes(
            (SettingKeys.RadarrUrl, "http://radarr:7878"),
            (SettingKeys.RadarrApiKey, ArrFixtures.RadarrKey),
            (SettingKeys.SonarrUrl, "http://sonarr:8989"),
            (SettingKeys.SonarrApiKey, ArrFixtures.SonarrKey),
            (SettingKeys.MaxConcurrentDownloads, 3)));
        var users = new PufferfinUserSettingsStore(Path.Combine(_directory, "users.json"), NullLogger<PufferfinUserSettingsStore>.Instance);
        _processor = new RequestProcessor(_store, _configuration, users, [_workflow], _time, NullLogger<RequestProcessor>.Instance);
        _service = new RequestService(_store, _processor, _configuration, users, new RequestTestKit.FakeMetadata(), new RequestTestKit.FakeAudiobooks(), _time);
        _store.Changed += (_, _) => _activeSnapshots.Add(_store.GetAll().Count(r => r.TakesSlot));
    }

    public void Dispose()
    {
        _processor.Dispose();
        _context.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<MediaRequest> Create(Guid user, int tmdbId = 438631)
    {
        _workflow.Titles.TryAdd(tmdbId, true);
        var outcome = await _service.CreateAsync(user, true, new RequestInput(RequestKind.Movie, tmdbId), CancellationToken.None);
        return outcome.Request!;
    }

    [Fact]
    public async Task MaxConcurrentDownloads_IsRespected_UnderLoad()
    {
        // Twenty users request at once while rounds run.
        var cancellationToken = TestContext.Current.CancellationToken;
        var creations = Enumerable.Range(0, 20).Select(i => Task.Run(() => Create(Guid.NewGuid(), 1000 + i), cancellationToken)).ToList();
        var rounds = Task.Run(
            async () =>
            {
                for (var i = 0; i < 5; i++)
                {
                    await _processor.RunRoundAsync(cancellationToken);
                }
            },
            cancellationToken);
        await Task.WhenAll(creations.Append(rounds));
        await _processor.RunRoundAsync(CancellationToken.None);

        var all = _store.GetAll();
        Assert.Equal(3, all.Count(r => r.State == RequestState.Downloading));
        Assert.Equal(17, all.Count(r => r.State == RequestState.Queued));
        Assert.True(_activeSnapshots.Max() <= 3);

        // A download finishing frees exactly one slot, taken by the next in queue order.
        var finished = all.First(r => r.State == RequestState.Downloading);
        _workflow.Finished.Add(finished.Id);
        var nextInLine = all.Where(r => r.State == RequestState.Queued).OrderBy(r => r.Position).First();
        await _processor.RunRoundAsync(CancellationToken.None);

        all = _store.GetAll();
        Assert.Equal(RequestState.Available, all.Single(r => r.Id.Equals(finished.Id)).State);
        Assert.Equal(RequestState.Downloading, all.Single(r => r.Id.Equals(nextInLine.Id)).State);
        Assert.Equal(3, all.Count(r => r.State == RequestState.Downloading));
        Assert.True(_activeSnapshots.Max() <= 3);
    }

    [Fact]
    public async Task TheQueueOrder_CanBeChanged()
    {
        _configuration.Update(StoreTestContext.Changes((SettingKeys.MaxConcurrentDownloads, 1)));
        var first = await Create(Guid.NewGuid(), 1);
        var second = await Create(Guid.NewGuid(), 2);
        var third = await Create(Guid.NewGuid(), 3);

        Assert.True(_store.Move(third.Id, 0));
        await _processor.RunRoundAsync(CancellationToken.None);

        Assert.Equal(RequestState.Downloading, _store.Get(third.Id)!.State);
        Assert.Equal(RequestState.Queued, _store.Get(first.Id)!.State);
        Assert.Equal(RequestState.Queued, _store.Get(second.Id)!.State);
    }

    [Fact]
    public async Task ARequestWaitingForSources_DoesNotHoldASlot_AndIsSearchedAgainLater()
    {
        _configuration.Update(StoreTestContext.Changes((SettingKeys.MaxConcurrentDownloads, 1)));
        var waiting = await Create(Guid.NewGuid(), 1);
        _workflow.NoSources.Add(1);
        await _processor.RunRoundAsync(CancellationToken.None);
        Assert.Equal(RequestState.WaitingForSources, _store.Get(waiting.Id)!.State);

        var other = await Create(Guid.NewGuid(), 2);
        await _processor.RunRoundAsync(CancellationToken.None);
        Assert.Equal(RequestState.Downloading, _store.Get(other.Id)!.State);

        // Later, with the slot free again, the waiting request is searched again.
        _workflow.NoSources.Remove(1);
        _workflow.Finished.Add(other.Id);
        _time.Advance(TimeSpan.FromHours(7));
        await _processor.RunRoundAsync(CancellationToken.None);
        Assert.Equal(RequestState.Downloading, _store.Get(waiting.Id)!.State);
    }

    [Fact]
    public async Task ACancellationDuringASlowSearch_Wins()
    {
        var request = await Create(Guid.NewGuid());
        _workflow.DuringSearch = id => _service.Cancel(id, Guid.Empty, true);

        await _processor.RunRoundAsync(CancellationToken.None);

        Assert.Equal(RequestState.Cancelled, _store.Get(request.Id)!.State);
    }

    [Fact]
    public async Task ARequestNeedingApproval_WaitsForAnAdministrator()
    {
        var user = Guid.NewGuid();
        _store.SetPermissions(user, new RequestPermissions { RequiresApproval = true });
        var outcome = await _service.CreateAsync(user, false, new RequestInput(RequestKind.Movie, 438631), CancellationToken.None);

        await _processor.RunRoundAsync(CancellationToken.None);
        Assert.Equal(RequestState.PendingApproval, _store.Get(outcome.Request!.Id)!.State);

        Assert.Equal(RequestChangeOutcome.Done, _service.Approve(outcome.Request.Id, Guid.NewGuid()).Outcome);
        await _processor.RunRoundAsync(CancellationToken.None);
        Assert.Equal(RequestState.Downloading, _store.Get(outcome.Request.Id)!.State);
    }

    [Fact]
    public async Task AnOldRequestForABookToRead_IsLeftAsItIs_AndHoldsNoSlot()
    {
        _configuration.Update(StoreTestContext.Changes((SettingKeys.MaxConcurrentDownloads, 1)));
        var book = _store.Add(new MediaRequest
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Kind = RequestKind.Ebook,
            TmdbId = 0,
            BookProvider = "openlibrary",
            BookId = "OL82563W",
            Title = "Harry Potter and the Philosopher's Stone",
            Language = "fr",
            State = RequestState.Queued,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow()
        });
        var film = await Create(Guid.NewGuid());

        await _processor.RunRoundAsync(CancellationToken.None);
        await _processor.RunRoundAsync(CancellationToken.None);

        Assert.Equal((RequestState.Queued, book.Version), (_store.Get(book.Id)!.State, _store.Get(book.Id)!.Version));
        Assert.Equal(RequestState.Downloading, _store.Get(film.Id)!.State);
    }

    private sealed class FakeWorkflow : IRequestWorkflow
    {
        public ConcurrentDictionary<int, bool> Titles { get; } = new();

        public HashSet<Guid> Finished { get; } = [];

        public HashSet<int> NoSources { get; } = [];

        public Action<Guid>? DuringSearch { get; set; }

        // Like the real workflows: none handles a book to read any more.
        public bool Handles(RequestKind kind) => kind != RequestKind.Ebook;

        public Task<MediaRequest> SearchAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken)
        {
            DuringSearch?.Invoke(request.Id);
            return Task.FromResult(NoSources.Contains(request.TmdbId)
                ? request.Wait(context.Now, new RequestProblem(RequestProblemCode.NoRelease, "No release."))
                : request.To(RequestState.Downloading, context.Now) with { ArrId = 1 });
        }

        public Task<MediaRequest> TrackAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken)
            => Task.FromResult(Finished.Contains(request.Id) ? request.To(RequestState.Available, context.Now) : request);
    }
}
