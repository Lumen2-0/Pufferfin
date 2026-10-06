using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Pufferfin.Server.Api;
using Pufferfin.Server.Deletion;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Tests.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// Deleting a title: safe by default, private torrents keep seeding until their minimum, the immediate mode needs a
/// confirmation and an administrator.
/// </summary>
public sealed class DeletionTests : IDisposable
{
    private static readonly Guid _movieItem = Guid.NewGuid();
    private static readonly Guid _episodeItem = Guid.NewGuid();
    private static readonly Guid _admin = Guid.NewGuid();
    private static readonly Guid _user = Guid.NewGuid();

    private readonly SeedingTestKit _kit = new();
    private readonly FakeLibraryContent _library = new();
    private readonly RequestTestKit.FakeLibrary _mediaLibrary = new();
    private readonly RequestStore _requests;
    private readonly ContentDeletionService _service;

    public DeletionTests()
    {
        _requests = new RequestStore(Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"), "requests.json"), _kit.Logs.CreateLogger<RequestStore>());
        _library.Titles[_movieItem] = new LibraryTitle(_movieItem, LibraryTitleKind.Movie, "Dune", 438631, null, null, null, []);
        _library.Titles[_episodeItem] = new LibraryTitle(_episodeItem, LibraryTitleKind.Episode, "Show S01E01", 1399, 121361, 1, 1, []);

        // Radarr has the film, grabbed twice: from a private tracker, then from a public one.
        _kit.Server.Add("movie", new JsonObject { ["id"] = 50, ["title"] = "Dune", ["tmdbId"] = 438631, ["hasFile"] = true });
        _kit.Server.Resource("history/movie").Add(Grab("PRIVATE1", movieId: 50));
        _kit.Server.Resource("history/movie").Add(Grab("PUBLIC1", movieId: 50));
        _kit.AddTorrent("private1", isPrivate: true, ratio: 4, seededDays: 2);
        _kit.AddTorrent("public1", isPrivate: false, ratio: 0.1, seededDays: 1);
        _kit.AddTorrent("unrelated", isPrivate: false, ratio: 0.1, seededDays: 1);

        var resolver = new TitleTorrentResolver(_kit.Arr, _kit.QBittorrent, new RequestTestKit.FakeMetadata(), _kit.Links, _kit.Logs.CreateLogger<TitleTorrentResolver>());
        _service = new ContentDeletionService(
            _library,
            _mediaLibrary,
            resolver,
            _kit.Arr,
            _kit.QBittorrent,
            _kit.Store,
            _requests,
            _kit.Configuration,
            _kit.Clock,
            _kit.Logs.CreateLogger<ContentDeletionService>());
    }

    private IEnumerable<(HttpMethod Method, string Path)> ArrDeletes
        => _kit.Server.Calls.Where(c => c.Method == HttpMethod.Delete).Select(c => (c.Method, c.Path));

    public void Dispose() => _kit.Dispose();

    [Fact]
    public void DefaultMode_IsTheSafeDeletion()
    {
        Assert.Equal(DeletionMode.Safe, default(DeletionMode));

        foreach (var name in new[] { nameof(PufferfinLibraryController.DeletePufferfinLibraryItem), nameof(PufferfinLibraryController.GetPufferfinDeletionPlan) })
        {
            var parameters = typeof(PufferfinLibraryController).GetMethod(name)!.GetParameters();
            Assert.Equal(DeletionMode.Safe, parameters.Single(p => p.Name == "mode").DefaultValue);
            if (parameters.SingleOrDefault(p => p.Name == "confirm") is { } confirm)
            {
                Assert.Equal(false, confirm.DefaultValue);
            }
        }
    }

    [Fact]
    public async Task DefaultDeletion_KeepsThePrivateTorrentSeeding_AndRemovesThePublicOne()
    {
        var outcome = await _service.DeleteAsync(_movieItem, default, confirmed: false, _admin, isAdministrator: true, CancellationToken.None);

        Assert.Equal(DeletionStatus.Deleted, outcome.Status);
        Assert.Equal(DeletionMode.Safe, outcome.Plan!.Mode);
        Assert.Equal(TorrentAction.KeepUntilMinimum, outcome.Plan.Torrents.Single(t => t.Hash == "private1").Action);
        Assert.Equal(TorrentAction.RemoveNow, outcome.Plan.Torrents.Single(t => t.Hash == "public1").Action);
        Assert.Equal(new[] { "public1" }, Assert.Single(_kit.Deletions));
        Assert.Equal("private1", Assert.Single(_kit.Store.GetPendingReleases()).Hash);
        Assert.Contains((HttpMethod.Delete, "movie/50?deleteFiles=true&addImportExclusion=false"), ArrDeletes);
        Assert.DoesNotContain(outcome.Plan.Torrents, t => t.Hash == "unrelated");
    }

    [Fact]
    public async Task SafeDeletion_ReleasesThePrivateTorrent_OnceTheHourlyTaskSeesItsMinimum()
    {
        await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        var before = await _kit.Runner.RunCycleAsync(CancellationToken.None);
        _kit.Server.Resource("torrents/info").OfType<JsonObject>().Single(t => t["hash"]!.GetValue<string>() == "private1")["seeding_time"] = (long)TimeSpan.FromDays(7).TotalSeconds;
        var after = await _kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Empty(before.Purged);
        var purge = Assert.Single(after.Purged);
        Assert.Equal(("private1", PurgeReason.Released, "Dune"), (purge.Hash, purge.Reason, purge.Title));
    }

    [Fact]
    public async Task SafeDeletion_TreatsATorrentOfUnknownPrivacyAsPrivate()
    {
        _kit.Server.Resource("torrents/info").OfType<JsonObject>().Single(t => t["hash"]!.GetValue<string>() == "private1").Remove("private");

        var outcome = await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        var plan = outcome.Plan!.Torrents.Single(t => t.Hash == "private1");
        Assert.Null(plan.IsPrivate);
        Assert.Equal(TorrentAction.KeepUntilMinimum, plan.Action);
    }

    [Fact]
    public async Task SafeDeletion_ReadsThePrivacyFromThePropertiesOfOlderQBittorrent()
    {
        var torrent = _kit.Server.Resource("torrents/info").OfType<JsonObject>().Single(t => t["hash"]!.GetValue<string>() == "public1");
        torrent.Remove("private");
        _kit.Properties["public1"] = false;

        var outcome = await _service.PreviewAsync(_movieItem, DeletionMode.Safe, _admin, true, CancellationToken.None);

        Assert.Equal(TorrentAction.RemoveNow, outcome.Plan!.Torrents.Single(t => t.Hash == "public1").Action);
    }

    [Fact]
    public async Task SafeDeletion_RemovesAPrivateTorrentPastItsMinimumNow()
    {
        _kit.Server.Resource("torrents/info").OfType<JsonObject>().Single(t => t["hash"]!.GetValue<string>() == "private1")["seeding_time"] = (long)TimeSpan.FromDays(8).TotalSeconds;

        await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        Assert.Equal(new[] { "private1", "public1" }, Assert.Single(_kit.Deletions).Order());
        Assert.Empty(_kit.Store.GetPendingReleases());
    }

    [Fact]
    public async Task ImmediateDeletion_WithoutConfirmation_DeletesNothing_AndWarns()
    {
        var outcome = await _service.DeleteAsync(_movieItem, DeletionMode.Immediate, confirmed: false, _admin, true, CancellationToken.None);

        Assert.Equal(DeletionStatus.ConfirmationRequired, outcome.Status);
        Assert.Equal(1, outcome.Plan!.PrivateBeforeMinimum);
        Assert.Contains("banned", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(ArrDeletes);
        Assert.Empty(_kit.Deletions);
        Assert.Empty(_kit.Store.GetPendingReleases());
    }

    [Fact]
    public async Task ImmediateDeletion_Confirmed_RemovesEveryTorrentNow()
    {
        var outcome = await _service.DeleteAsync(_movieItem, DeletionMode.Immediate, confirmed: true, _admin, true, CancellationToken.None);

        Assert.Equal(DeletionStatus.Deleted, outcome.Status);
        Assert.Equal(new[] { "private1", "public1" }, Assert.Single(_kit.Deletions).Order());
        Assert.Empty(_kit.Store.GetPendingReleases());
        Assert.All(_kit.Store.GetRecentPurges(), p => Assert.Equal(PurgeReason.ImmediateDeletion, p.Reason));
    }

    [Fact]
    public async Task ImmediateDeletion_IsForAdministratorsOnly()
    {
        _requests.SetPermissions(_user, new RequestPermissions { CanDelete = true });

        var outcome = await _service.DeleteAsync(_movieItem, DeletionMode.Immediate, confirmed: true, _user, false, CancellationToken.None);

        Assert.Equal(DeletionStatus.Forbidden, outcome.Status);
        Assert.Empty(ArrDeletes);
        Assert.Empty(_kit.Deletions);
    }

    [Fact]
    public async Task Deletion_NeedsThePermission()
    {
        var refused = await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _user, false, CancellationToken.None);
        _requests.SetPermissions(_user, new RequestPermissions { CanDelete = true });
        var allowed = await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _user, false, CancellationToken.None);

        Assert.Equal(DeletionStatus.Forbidden, refused.Status);
        Assert.Equal(DeletionStatus.Deleted, allowed.Status);
        Assert.False(RequestPermissions.Default.CanDelete);
    }

    [Fact]
    public async Task WhenRadarrFails_NothingIsDeleted_AndNoTorrentStaysWaiting()
    {
        _kit.Server.Overrides.Insert(0, (method, path, _) => method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null);

        var outcome = await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        Assert.Equal(DeletionStatus.ServiceUnavailable, outcome.Status);
        Assert.Empty(_kit.Deletions);
        Assert.Empty(_kit.Store.GetPendingReleases());
    }

    [Fact]
    public async Task WhenQBittorrentCannotSayWhichTorrentsArePrivate_NothingIsDeleted()
    {
        _kit.QBittorrentDown = true;

        var outcome = await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        Assert.Equal(DeletionStatus.ServiceUnavailable, outcome.Status);
        Assert.Empty(ArrDeletes);
    }

    [Fact]
    public async Task Episode_KeepsTheSeasonPackOtherEpisodesStillUse_AndUnmonitorsBeforeDeleting()
    {
        _kit.Server.Add("series", new JsonObject { ["id"] = 7, ["title"] = "Show", ["tvdbId"] = 121361 });
        _kit.Server.Add("episode", new JsonObject { ["id"] = 101, ["seriesId"] = 7, ["seasonNumber"] = 1, ["episodeNumber"] = 1, ["hasFile"] = true, ["episodeFileId"] = 1001 });
        _kit.Server.Add("episode", new JsonObject { ["id"] = 102, ["seriesId"] = 7, ["seasonNumber"] = 1, ["episodeNumber"] = 2, ["hasFile"] = true, ["episodeFileId"] = 1002 });
        foreach (var episode in new[] { 101, 102 })
        {
            _kit.Server.Resource("history/series").Add(Grab("PACK", episodeId: episode));
        }

        _kit.Server.Resource("history/series").Add(Grab("SOLO", episodeId: 101));
        _kit.AddTorrent("pack", category: "sonarr", isPrivate: false, seededDays: 1);
        _kit.AddTorrent("solo", category: "sonarr", isPrivate: false, seededDays: 1);

        var outcome = await _service.DeleteAsync(_episodeItem, DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        Assert.Equal(DeletionStatus.Deleted, outcome.Status);
        Assert.Equal(TorrentAction.KeepShared, outcome.Plan!.Torrents.Single(t => t.Hash == "pack").Action);
        Assert.Equal(new[] { "solo" }, Assert.Single(_kit.Deletions));
        var writes = _kit.Server.Writes.Select(w => w.Path).ToList();
        Assert.True(writes.IndexOf("episode/monitor") < writes.IndexOf("episodefile/1001"));
        Assert.DoesNotContain("episodefile/1002", writes);
        Assert.Contains("\"monitored\":false", _kit.Server.Writes.Single(w => w.Path == "episode/monitor").Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownItem_IsNotFound()
    {
        var outcome = await _service.DeleteAsync(Guid.NewGuid(), DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        Assert.Equal(DeletionStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task NoOutcomeNorLog_ContainsAPasskey()
    {
        var preview = await _service.PreviewAsync(_movieItem, DeletionMode.Immediate, _admin, true, CancellationToken.None);
        var deleted = await _service.DeleteAsync(_movieItem, DeletionMode.Safe, false, _admin, true, CancellationToken.None);

        foreach (var outcome in new[] { preview, deleted })
        {
            Assert.DoesNotContain(SeedingTestKit.Passkey, JsonSerializer.Serialize(outcome, JsonDefaults.Options), StringComparison.Ordinal);
        }

        Assert.All(_kit.Logs.Entries, entry => Assert.DoesNotContain(SeedingTestKit.Passkey, entry, StringComparison.Ordinal));
    }

    private static JsonObject Grab(string downloadId, int? movieId = null, int? episodeId = null)
        => new()
        {
            ["id"] = Random.Shared.Next(1, 100000),
            ["eventType"] = "grabbed",
            ["movieId"] = movieId,
            ["episodeId"] = episodeId,
            ["seriesId"] = episodeId is null ? null : 7,
            ["downloadId"] = downloadId,
            ["sourceTitle"] = "Release." + downloadId,
            ["date"] = SeedingTestKit.Now.AddDays(-3),
            ["data"] = new JsonObject { ["downloadUrl"] = "https://tracker.example/download/1?passkey=" + SeedingTestKit.Passkey }
        };

    private sealed class FakeLibraryContent : ILibraryContent
    {
        public Dictionary<Guid, LibraryTitle> Titles { get; } = [];

        public List<Guid> Deleted { get; } = [];

        public LibraryTitle? Find(Guid itemId) => Titles.GetValueOrDefault(itemId);

        public void DeleteFiles(Guid itemId) => Deleted.Add(itemId);
    }
}
