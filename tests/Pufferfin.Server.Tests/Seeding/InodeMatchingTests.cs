using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Deletion;
using Pufferfin.Server.Storage;
using Pufferfin.Server.Tests.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// Audiobooks are linked to their torrents by inode: the library file is a hard link of the downloaded file.
/// </summary>
public sealed class InodeMatchingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));

    public InodeMatchingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "downloads", "audiobooks", "Dune"));
        Directory.CreateDirectory(Path.Combine(_root, "downloads", "audiobooks", "Saga"));
        Directory.CreateDirectory(Path.Combine(_root, "media", "audiobooks", "Frank Herbert", "Dune"));
        Directory.CreateDirectory(Path.Combine(_root, "media", "audiobooks", "Saga"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Matcher_FindsTheTorrentHoldingADeletedFile_ByInodeOnly()
    {
        var book = new FileIdentity(1, 500, 2);
        var other = new FileIdentity(1, 600, 2);

        var matches = InodeTorrentMatcher.Match(
            [book],
            [
                ("dune", [new FileIdentity(1, 500, 2)]),
                ("other", [other]),
                ("same-inode-other-device", [new FileIdentity(2, 500, 2)])
            ]);

        var match = Assert.Single(matches);
        Assert.Equal("dune", match.Hash);
        Assert.True(match.FullyCovered);
    }

    [Fact]
    public void Matcher_KeepsATorrentWhoseOtherFilesTheLibraryStillUses()
    {
        var deleted = new FileIdentity(1, 500, 2);
        var stillInLibrary = new FileIdentity(1, 501, 2);
        var onlyInDownloads = new FileIdentity(1, 502, 1);

        var shared = Assert.Single(InodeTorrentMatcher.Match([deleted], [("saga", [deleted, stillInLibrary])]));
        var alone = Assert.Single(InodeTorrentMatcher.Match([deleted], [("saga", [deleted, onlyInDownloads])]));

        Assert.False(shared.FullyCovered);
        Assert.True(alone.FullyCovered);
    }

    [Fact]
    public void NativeLinks_SeeAHardLinkAsTheSameFile()
    {
        var links = new NativeFileLinks();
        Assert.SkipUnless(links.IsSupported, "Inodes are only read on Linux and Windows.");
        var source = Path.Combine(_root, "downloads", "audiobooks", "Dune", "01.m4b");
        var target = Path.Combine(_root, "media", "audiobooks", "Frank Herbert", "Dune", "Dune - Part 1.m4b");
        File.WriteAllText(source, "audio");

        Assert.True(links.TryCreateHardLink(source, target));
        var a = links.GetIdentity(source);
        var b = links.GetIdentity(target);
        File.WriteAllText(Path.Combine(_root, "copy.m4b"), "audio");
        var copy = links.GetIdentity(Path.Combine(_root, "copy.m4b"));

        Assert.NotNull(a);
        Assert.True(a!.Value.IsSameFile(b!.Value));
        Assert.Equal(2u, a.Value.LinkCount);
        Assert.False(a.Value.IsSameFile(copy!.Value));
        Assert.Equal(1u, copy.Value.LinkCount);
        Assert.Null(links.GetIdentity(Path.Combine(_root, "missing.m4b")));
    }

    [Fact]
    public async Task Resolver_LinksAnAudiobookToItsTorrent_ThroughRealHardLinks()
    {
        var links = new NativeFileLinks();
        Assert.SkipUnless(links.IsSupported, "Inodes are only read on Linux and Windows.");

        // Dune came alone; the saga torrent holds Dune too, and a second book still in the library.
        var downloads = Path.Combine(_root, "downloads");
        Link(links, Path.Combine("audiobooks", "Dune", "01.m4b"), Path.Combine("audiobooks", "Frank Herbert", "Dune", "Dune.m4b"));
        Link(links, Path.Combine("audiobooks", "Saga", "dune.m4b"), Path.Combine("audiobooks", "Frank Herbert", "Dune", "Dune (saga).m4b"));
        Link(links, Path.Combine("audiobooks", "Saga", "messiah.m4b"), Path.Combine("audiobooks", "Saga", "Messiah.m4b"));

        using var kit = new SeedingTestKit((SettingKeys.DownloadsPath, downloads), (SettingKeys.QBittorrentDownloadsPath, "/downloads"));
        kit.AddTorrent("dune", category: MediaLayout.AudiobooksCategory, savePath: "/downloads/audiobooks");
        kit.AddTorrent("saga", category: MediaLayout.AudiobooksCategory, savePath: "/downloads/audiobooks");
        kit.AddTorrent("film", category: MediaLayout.RadarrCategory, savePath: "/downloads/radarr");
        kit.Files["dune"] = [TorrentFile("Dune/01.m4b")];
        kit.Files["saga"] = [TorrentFile("Saga/dune.m4b"), TorrentFile("Saga/messiah.m4b")];
        kit.Files["film"] = [TorrentFile("Dune/01.m4b")];

        var media = Path.Combine(_root, "media", "audiobooks", "Frank Herbert", "Dune");
        var title = new LibraryTitle(Guid.NewGuid(), LibraryTitleKind.Audiobook, "Dune", null, null, null, null, [Path.Combine(media, "Dune.m4b"), Path.Combine(media, "Dune (saga).m4b")]);
        var resolver = new TitleTorrentResolver(kit.Arr, kit.QBittorrent, new RequestTestKit.FakeMetadata(), links, NullLogger<TitleTorrentResolver>.Instance);

        var resolved = await resolver.ResolveAsync(title, kit.Configuration.Current.Settings, CancellationToken.None);

        Assert.Equal(new[] { "dune" }, resolved.Releasable);
        Assert.Equal(new[] { "saga" }, resolved.Shared);
    }

    private static JsonObject TorrentFile(string name) => new() { ["name"] = name, ["size"] = 5, ["priority"] = 1 };

    private void Link(NativeFileLinks links, string download, string library)
    {
        var source = Path.Combine(_root, "downloads", download);
        File.WriteAllText(source, download);
        Assert.True(links.TryCreateHardLink(source, Path.Combine(_root, "media", library)));
    }
}
