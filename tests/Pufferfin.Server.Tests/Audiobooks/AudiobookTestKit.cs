using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Workflow;
using Pufferfin.Server.Tests.AutoConfig;
using Pufferfin.Server.Tests.Requests;
using Pufferfin.Server.Tests.TestHelpers;

namespace Pufferfin.Server.Tests.Audiobooks;

/// <summary>
/// A Shelfmark and a qBittorrent in memory, a temporary disk, and the audiobook workflow on top of them.
/// </summary>
public sealed class AudiobookTestKit : IDisposable
{
    public const string ShelfmarkKey = "SECRET-shelfmark-key-5e1f";
    public const string TorrentHash = "8c4f0b1e2d3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c";

    public AudiobookTestKit()
    {
        Shelfmark.Overrides.Add((method, path, body) =>
        {
            if (method == HttpMethod.Get && path.StartsWith("releases?", StringComparison.Ordinal))
            {
                Searches.Enqueue(path);
                return SearchAnswer?.Invoke() ?? FakeArrServer.Json(new JsonObject { ["releases"] = new JsonArray(Releases.Select(r => (JsonNode)r.DeepClone()).ToArray()) });
            }

            if (method == HttpMethod.Post && path == "releases/download")
            {
                Queued.Enqueue(JsonNode.Parse(body!)!.AsObject());
                return FakeArrServer.Json(new JsonObject { ["status"] = "queued", ["priority"] = 0 });
            }

            if (method == HttpMethod.Get && path == "status")
            {
                return FakeArrServer.Json(Status.DeepClone());
            }

            return null;
        });

        QBittorrent.Overrides.Add((method, path, _) =>
        {
            if (path == "torrents/info")
            {
                return FakeArrServer.Json(new JsonArray(Torrents.Select(t => (JsonNode)new JsonObject { ["hash"] = t.Hash, ["name"] = t.Name, ["category"] = "audiobooks", ["save_path"] = Disk.Downloads }).ToArray()));
            }

            if (path.StartsWith("torrents/files?hash=", StringComparison.Ordinal))
            {
                var hash = path["torrents/files?hash=".Length..];
                var torrent = Torrents.FirstOrDefault(t => t.Hash == hash);
                return FakeArrServer.Json(new JsonArray((torrent.Files ?? []).Select(f => (JsonNode)new JsonObject { ["name"] = f, ["size"] = 5, ["priority"] = 1 }).ToArray()));
            }

            return null;
        });

        var qBittorrent = new QBittorrentClient(QBittorrent, NullLogger<QBittorrentClient>.Instance, TimeSpan.FromSeconds(5));
        Locator = new AudiobookTorrentLocator(qBittorrent, Disk.Links);
        Workflow = new AudiobookWorkflow(
            new ShelfmarkClient(FakeArrServer.CreateHttp(Shelfmark)),
            Books,
            Disk.Library,
            Locator,
            Library,
            Logs.CreateLogger<AudiobookWorkflow>());
    }

    public FakeArrServer Shelfmark { get; } = new();

    public FakeArrServer QBittorrent { get; } = new();

    public AudiobookDisk Disk { get; } = new();

    public RequestTestKit.FakeAudiobooks Books { get; } = new();

    public RequestTestKit.FakeLibrary Library { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public AudiobookTorrentLocator Locator { get; }

    public AudiobookWorkflow Workflow { get; }

    public Collection<JsonObject> Releases { get; } = [];

    public Func<HttpResponseMessage>? SearchAnswer { get; set; }

    public ConcurrentQueue<string> Searches { get; } = new();

    public ConcurrentQueue<JsonObject> Queued { get; } = new();

    // { "<status>": { "<id>": { ... } } }, like /api/status.
    public JsonObject Status { get; } = [];

    public Collection<(string Hash, string Name, string[] Files)> Torrents { get; } = [];

    public static RequestContext Context(bool shelfmark = true, bool qBittorrent = true, DateTimeOffset? now = null, AudiobookDisk? disk = null)
    {
        var changes = new List<(string, object?)> { (SettingKeys.PreferredAudioLanguages, "fr,en") };
        if (disk is not null)
        {
            changes.Add((SettingKeys.MediaPath, disk.Media));
            changes.Add((SettingKeys.DownloadsPath, disk.DownloadsRoot));
        }

        if (shelfmark)
        {
            changes.Add((SettingKeys.ShelfmarkUrl, "http://shelfmark:8084"));
            changes.Add((SettingKeys.ShelfmarkApiKey, ShelfmarkKey));
        }

        if (qBittorrent)
        {
            changes.Add((SettingKeys.QBittorrentUrl, "http://qbittorrent:8080"));
        }

        var settings = ArrFixtures.Settings(changes.ToArray());
        return new RequestContext(settings, now ?? RequestTestKit.Now, settings.PreferredAudioLanguages);
    }

    public static MediaRequest Request(string language = "fr", RequestState state = RequestState.Searching)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Kind = RequestKind.Audiobook,
            TmdbId = 0,
            Asin = RequestTestKit.FakeAudiobooks.Dune.Asin,
            Market = "fr",
            Title = "Dune",
            Language = language,
            State = state,
            CreatedAt = RequestTestKit.Now,
            UpdatedAt = RequestTestKit.Now
        };

    public static JsonObject Release(string title, int seeders = 10, string? id = null, string? language = null)
        => new()
        {
            ["source"] = "prowlarr",
            ["source_id"] = id ?? title,
            ["title"] = title,
            ["seeders"] = seeders,
            ["language"] = language,
            ["content_type"] = "audiobook",
            ["protocol"] = "torrent",
            ["download_url"] = "https://tracker.example/download?passkey=SECRET-passkey-77"
        };

    public void SetDownload(string status, string id, string title = "Dune", double progress = 0, string? downloadPath = null, string? message = null)
    {
        foreach (var (_, bucket) in Status)
        {
            bucket!.AsObject().Remove(id);
        }

        if (Status[status] is not JsonObject target)
        {
            target = [];
            Status[status] = target;
        }

        target[id] = new JsonObject
        {
            ["id"] = id,
            ["title"] = title,
            ["author"] = "Frank Herbert",
            ["content_type"] = "audiobook",
            ["progress"] = progress,
            ["status"] = status,
            ["status_message"] = message,
            ["download_path"] = downloadPath
        };
    }

    // A complete torrent in the downloads folder, as qBittorrent and Shelfmark leave it.
    public string CompleteTorrent(string name = "Frank Herbert - Dune [FR]")
    {
        Disk.Download(Path.Combine(name, "Dune - Part 1.m4b"));
        Disk.Download(Path.Combine(name, "Dune - Part 2.m4b"));
        Torrents.Add(("0000000000000000000000000000000000000000", "Another book", ["Another book/book.m4b"]));
        Disk.Download(Path.Combine("Another book", "book.m4b"));
        Torrents.Add((TorrentHash, name, [name + "/Dune - Part 1.m4b", name + "/Dune - Part 2.m4b"]));
        return Path.Combine(Disk.Downloads, name);
    }

    public void Dispose()
    {
        Logs.Dispose();
        Disk.Dispose();
        Shelfmark.Dispose();
        QBittorrent.Dispose();
    }
}
