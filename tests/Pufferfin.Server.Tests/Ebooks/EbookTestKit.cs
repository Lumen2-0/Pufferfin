using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Ebooks;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Integration;
using Pufferfin.Server.Requests.Workflow;
using Pufferfin.Server.Tests.Audiobooks;
using Pufferfin.Server.Tests.Requests;
using Pufferfin.Server.Tests.TestHelpers;

namespace Pufferfin.Server.Tests.Ebooks;

/// <summary>
/// A Shelfmark and a qBittorrent in memory, a temporary disk, and the ebook workflow on top of them.
/// </summary>
public sealed class EbookTestKit : IDisposable
{
    public const string TorrentHash = "5b2e8c1d0f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c";

    public EbookTestKit()
    {
        Shelfmark.Overrides.Add((method, path, body) =>
        {
            if (method == HttpMethod.Get && path.StartsWith("releases?", StringComparison.Ordinal))
            {
                Searches.Enqueue(path);
                return FakeArrServer.Json(new JsonObject { ["releases"] = new JsonArray(Releases.Select(r => (JsonNode)r.DeepClone()).ToArray()) });
            }

            if (method == HttpMethod.Get && path.StartsWith("metadata/book/", StringComparison.Ordinal))
            {
                var id = path["metadata/book/".Length..];
                return Books.TryGetValue(id, out var book)
                    ? FakeArrServer.Json(book.DeepClone())
                    : FakeArrServer.Json(new JsonObject { ["error"] = "Book not found" }, System.Net.HttpStatusCode.NotFound);
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
                return FakeArrServer.Json(new JsonArray(Torrents.Select(t => (JsonNode)new JsonObject { ["hash"] = t.Hash, ["name"] = t.Name, ["category"] = t.Category, ["save_path"] = Disk.Downloads }).ToArray()));
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
        Library = new EbookLibrary(Disk.Covers, Disk.Links, () => LibraryRoot, NullLogger<EbookLibrary>.Instance);
        Workflow = new EbookWorkflow(
            new ShelfmarkClient(FakeArrServer.CreateHttp(Shelfmark)),
            new ShelfmarkEbookMetadata(new ShelfmarkClient(FakeArrServer.CreateHttp(Shelfmark))),
            Library,
            new AudiobookTorrentLocator(qBittorrent, Disk.Links),
            Media,
            Logs.CreateLogger<EbookWorkflow>());
        Books["hardcover/312460"] = Book();
    }

    public FakeArrServer Shelfmark { get; } = new();

    public FakeArrServer QBittorrent { get; } = new();

    public AudiobookDisk Disk { get; } = new();

    public string LibraryRoot => Path.Combine(Disk.Media, EbookLibrary.FolderName);

    public EbookLibrary Library { get; }

    public RequestTestKit.FakeLibrary Media { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public EbookWorkflow Workflow { get; }

    // By "provider/id", like /api/metadata/book/{provider}/{id}.
    public Dictionary<string, JsonObject> Books { get; } = new(StringComparer.Ordinal);

    public Collection<JsonObject> Releases { get; } = [];

    public ConcurrentQueue<string> Searches { get; } = new();

    public ConcurrentQueue<JsonObject> Queued { get; } = new();

    // { "<status>": { "<id>": { ... } } }, like /api/status.
    public JsonObject Status { get; } = [];

    public Collection<(string Hash, string Name, string Category, string[] Files)> Torrents { get; } = [];

    // Dune as Hardcover describes it, through Shelfmark, with its cover proxied by the Shelfmark cover cache.
    public static JsonObject Book(string title = "Dune", string? subtitle = null, string language = "French")
        => new()
        {
            ["provider"] = "hardcover",
            ["provider_id"] = "312460",
            ["provider_display_name"] = "Hardcover",
            ["title"] = title,
            ["subtitle"] = subtitle,
            ["authors"] = new JsonArray("Frank Herbert"),
            ["isbn_13"] = "9782266320481",
            ["cover_url"] = "/api/covers/hardcover_312460?url=" + Convert.ToBase64String("https://assets.hardcover.app/dune.jpg"u8.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            ["publish_year"] = 1965,
            ["language"] = language,
            ["series_name"] = "Dune",
            ["series_position"] = 1.0
        };

    public static MediaRequest Request(string language = "fr", RequestState state = RequestState.Searching)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Kind = RequestKind.Ebook,
            TmdbId = 0,
            BookProvider = "hardcover",
            BookId = "312460",
            Title = "Dune",
            Language = language,
            State = state,
            CreatedAt = RequestTestKit.Now,
            UpdatedAt = RequestTestKit.Now
        };

    public static JsonObject Release(string title, int? seeders = 10, string? id = null, string? format = "epub", string? language = null, string contentType = "ebook", string protocol = "torrent")
        => new()
        {
            ["source"] = protocol == "torrent" ? "prowlarr" : "direct_download",
            ["source_id"] = id ?? title,
            ["title"] = title,
            ["format"] = format,
            ["seeders"] = seeders,
            ["language"] = language,
            ["content_type"] = contentType,
            ["protocol"] = protocol,
            ["download_url"] = "https://tracker.example/download?passkey=SECRET-passkey-77"
        };

    public void SetDownload(string status, string id, string title = "Dune", double progress = 0, string? downloadPath = null, string contentType = "ebook")
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
            ["content_type"] = contentType,
            ["progress"] = progress,
            ["status"] = status,
            ["download_path"] = downloadPath
        };
    }

    // A complete book torrent in the downloads folder, as qBittorrent and Shelfmark leave it, next to another one.
    public string CompleteTorrent(string name = "Frank Herbert - Dune [FR]")
    {
        Disk.Download(Path.Combine("Another book", "book.epub"), "another");
        Torrents.Add(("0000000000000000000000000000000000000000", "Another book", MediaLayout.BooksCategory, ["Another book/book.epub"]));
        Disk.Download(Path.Combine(name, "Dune.epub"), "epub");
        Disk.Download(Path.Combine(name, "Dune.pdf"), "pdf, smaller");
        Disk.Download(Path.Combine(name, "Dune.mobi"), "mobi");
        Torrents.Add((TorrentHash, name, MediaLayout.BooksCategory, [name + "/Dune.epub", name + "/Dune.pdf", name + "/Dune.mobi"]));
        return Path.Combine(Disk.Downloads, name);
    }

    // A complete torrent holding a ZIP archive of the book, as Shelfmark leaves it when it hard links its book torrents.
    public string CompleteZipTorrent(string name = "Frank Herbert - Dune [FR].zip")
    {
        var path = Path.Combine(Disk.Downloads, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Write(zip, "Dune/readme.txt", "notes");
            Write(zip, "Dune/Dune.mobi", "mobi");
            Write(zip, "Dune/Dune.epub", "the epub of the book");
        }

        Torrents.Add((TorrentHash, name, MediaLayout.BooksCategory, [name]));
        return path;
    }

    public void Dispose()
    {
        Logs.Dispose();
        Disk.Dispose();
        Shelfmark.Dispose();
        QBittorrent.Dispose();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    public sealed class FakeLibraryFolders : ILibraryFolders
    {
        public List<string> Folders { get; } = [];

        public IReadOnlyList<string> GetBookLibraryFolders() => Folders;
    }
}
