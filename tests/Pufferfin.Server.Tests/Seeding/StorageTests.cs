using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Storage;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// The storage screen: real disk usage, the hard link advice, and the seeding state of each torrent.
/// </summary>
public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));
    private readonly string _media;
    private readonly string _downloads;

    public StorageTests()
    {
        _media = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
        _downloads = Directory.CreateDirectory(Path.Combine(_root, "downloads")).FullName;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void HardlinkCheck_WarnsWhenTheDisksDiffer()
    {
        using var kit = new SeedingTestKit();
        kit.Links.CanLink = false;
        var check = new HardlinkCheck(kit.Links, kit.Clock, kit.Logs.CreateLogger<HardlinkCheck>());

        var result = check.Check(new StoragePaths(_media, _downloads, _downloads, 90));

        Assert.Equal(HardlinkStatus.Impossible, result.Status);
        Assert.Contains("same disk", result.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_media));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_downloads));
    }

    [Fact]
    public void HardlinkCheck_WithRealLinks_OnOneDisk_SaysPossible_AndLeavesNoProbe()
    {
        var links = new NativeFileLinks();
        Assert.SkipUnless(links.IsSupported, "Hard links are only checked on Linux and Windows.");
        var check = new HardlinkCheck(links, TimeProvider.System, new Microsoft.Extensions.Logging.Abstractions.NullLogger<HardlinkCheck>());

        var result = check.Check(new StoragePaths(_media, _downloads, _downloads, 90));

        Assert.Equal(HardlinkStatus.Possible, result.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_media));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_downloads));
    }

    [Fact]
    public void HardlinkCheck_MissingDirectory_IsUnknown_NotAnError()
    {
        using var kit = new SeedingTestKit();
        var check = new HardlinkCheck(kit.Links, kit.Clock, kit.Logs.CreateLogger<HardlinkCheck>());

        var result = check.Check(new StoragePaths(Path.Combine(_root, "nope"), _downloads, _downloads, 90));

        Assert.Equal(HardlinkStatus.Unknown, result.Status);
    }

    [Fact]
    public void HardlinkCheck_KeepsItsResultAnHour()
    {
        using var kit = new SeedingTestKit();
        var check = new HardlinkCheck(kit.Links, kit.Clock, kit.Logs.CreateLogger<HardlinkCheck>());
        var paths = new StoragePaths(_media, _downloads, _downloads, 90);

        Assert.Equal(HardlinkStatus.Possible, check.Check(paths).Status);
        kit.Links.CanLink = false;
        Assert.Equal(HardlinkStatus.Possible, check.Check(paths).Status);
        Assert.Equal(HardlinkStatus.Impossible, check.Check(paths, refresh: true).Status);
        kit.Links.CanLink = true;
        kit.Clock.Advance(HardlinkCheck.CacheDuration);
        Assert.Equal(HardlinkStatus.Possible, check.Check(paths).Status);
    }

    [Fact]
    public async Task Report_AdvisesWhenHardLinksAreImpossible_AndWhenADiskReachesTheWarning()
    {
        using var kit = new SeedingTestKit((SettingKeys.MediaPath, _media), (SettingKeys.DownloadsPath, _downloads), (SettingKeys.StorageWarningPercent, 80));
        kit.Links.CanLink = false;
        var disks = new FakeDiskSpace { Disks = { [_media] = (1000, 150), [_downloads] = (1000, 600) } };

        var report = await Service(kit, disks).GetReportAsync(false, CancellationToken.None);

        Assert.Contains(report.Advice, a => a.Code == StorageAdviceCode.HardlinksImpossible);
        var almostFull = Assert.Single(report.Advice, a => a.Code == StorageAdviceCode.DiskAlmostFull);
        Assert.Contains(_media, almostFull.Message, StringComparison.Ordinal);
        Assert.Equal(85, report.Disks.Single(d => d.Role == StorageRole.Media).UsedPercent);
        Assert.True(report.Disks.Single(d => d.Role == StorageRole.Media).AboveWarning);
        Assert.False(report.Disks.Single(d => d.Role == StorageRole.Downloads).AboveWarning);
        Assert.Equal(80, report.Policy.WarningPercent);
    }

    [Fact]
    public async Task Report_GivesTheSeedingStateOfEachTorrent_AndWhatSeedingCosts()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0), (SettingKeys.MediaPath, _media), (SettingKeys.DownloadsPath, _downloads));
        kit.AddTorrent("young", ratio: 3, seededDays: 1);
        kit.AddTorrent("seeding", ratio: 0.2, seededDays: 9);
        kit.AddTorrent("due", ratio: 2, seededDays: 9);
        kit.AddTorrent("released", ratio: 0, seededDays: 2);
        kit.AddTorrent("downloading", progress: 0.3);
        kit.AddTorrent("manual", category: "manual", seededDays: 2);
        kit.Store.AddPendingReleases([new PendingRelease("released", "Arrival", true, SeedingTestKit.Now, Guid.NewGuid())]);
        kit.Files["young"] = [new JsonObject { ["name"] = "Young/film.mkv", ["size"] = 700, ["priority"] = 1 }];
        kit.Files["seeding"] = [new JsonObject { ["name"] = "Seeding/film.mkv", ["size"] = 300, ["priority"] = 1 }];
        kit.Links.Identities["/downloads/radarr/Young/film.mkv"] = new(1, 1, 2);
        kit.Links.Identities["/downloads/radarr/Seeding/film.mkv"] = new(1, 2, 1);
        kit.Server.Resource("history/since").Add(new JsonObject { ["movieId"] = 50, ["downloadId"] = "YOUNG", ["eventType"] = "grabbed", ["movie"] = new JsonObject { ["title"] = "Dune", ["tmdbId"] = 438631 } });

        var report = await Service(kit, new FakeDiskSpace()).GetReportAsync(false, CancellationToken.None);

        var states = report.Torrents.ToDictionary(t => t.Hash);
        Assert.Equal(SeedState.Protected, states["young"].State);
        Assert.Equal("Dune", states["young"].Title);
        Assert.Equal(SeedingTestKit.Now + TimeSpan.FromDays(6), states["young"].ProtectedUntil);
        Assert.True(states["young"].SharesSpaceWithLibrary);
        Assert.Equal(SeedState.Seeding, states["seeding"].State);
        Assert.False(states["seeding"].SharesSpaceWithLibrary);
        Assert.Equal(SeedState.DueForPurge, states["due"].State);
        Assert.Equal(SeedState.WaitingForRelease, states["released"].State);
        Assert.Equal("Arrival", states["released"].Title);
        Assert.Equal(SeedState.Downloading, states["downloading"].State);
        Assert.DoesNotContain("manual", states.Keys);
        Assert.Equal(700, report.Content.SharedWithLibraryBytes);
        Assert.Equal(300, report.Content.SeedingOnlyBytes);
    }

    [Fact]
    public async Task Report_NeverContainsAPasskey()
    {
        using var kit = new SeedingTestKit();
        kit.AddTorrent("young", ratio: 3, seededDays: 1);

        var report = await Service(kit, new FakeDiskSpace()).GetReportAsync(true, CancellationToken.None);

        Assert.Single(report.Torrents);
        Assert.DoesNotContain(SeedingTestKit.Passkey, JsonSerializer.Serialize(report, JsonDefaults.Options), StringComparison.Ordinal);
        Assert.DoesNotContain("tracker.example", JsonSerializer.Serialize(report, JsonDefaults.Options), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_WhenQBittorrentIsDown_StillGivesTheDisks()
    {
        using var kit = new SeedingTestKit((SettingKeys.MediaPath, _media), (SettingKeys.DownloadsPath, _downloads));
        kit.QBittorrentDown = true;
        var disks = new FakeDiskSpace { Disks = { [_media] = (1000, 500), [_downloads] = (1000, 500) } };

        var report = await Service(kit, disks).GetReportAsync(false, CancellationToken.None);

        Assert.Contains(report.Advice, a => a.Code == StorageAdviceCode.QBittorrentUnavailable);
        Assert.All(report.Disks, d => Assert.True(d.Available));
    }

    private static StorageService Service(SeedingTestKit kit, IDiskSpace disks)
        => new(
            kit.Configuration,
            kit.QBittorrent,
            kit.Arr,
            kit.Store,
            kit.Runner,
            new HardlinkCheck(kit.Links, kit.Clock, kit.Logs.CreateLogger<HardlinkCheck>()),
            kit.Links,
            disks,
            kit.Clock,
            kit.Logs.CreateLogger<StorageService>());

    private sealed class FakeDiskSpace : IDiskSpace
    {
        public Dictionary<string, (long Total, long Free)> Disks { get; } = new(StringComparer.Ordinal);

        public (long Total, long Free)? Read(string path) => Disks.TryGetValue(path, out var space) ? space : null;
    }
}
