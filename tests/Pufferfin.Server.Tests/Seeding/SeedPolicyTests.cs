using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Tests.AutoConfig;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// The seeding policy: the minimum seeding time comes before everything, then the target ratio or the maximum age.
/// </summary>
public sealed class SeedPolicyTests
{
    private static readonly DateTimeOffset _now = SeedingTestKit.Now;

    private static SeedPolicy Policy(int minDays = 7, decimal? ratio = 1.0m, int? maxDays = 30)
        => SeedPolicy.From(ArrFixtures.Settings(
            (SettingKeys.SeedMinDays, minDays),
            (SettingKeys.SeedTargetRatio, ratio),
            (SettingKeys.SeedMaxDays, maxDays)));

    private static QBittorrentTorrent Torrent(double ratio, double seededDays, double completedDaysAgo, bool? isPrivate = true, double progress = 1)
        => new()
        {
            Hash = "abc",
            Category = MediaLayout.RadarrCategory,
            Progress = progress,
            Ratio = ratio,
            SeedingTime = (long)TimeSpan.FromDays(seededDays).TotalSeconds,
            CompletionOn = progress >= 1 ? (_now - TimeSpan.FromDays(completedDaysAgo)).ToUnixTimeSeconds() : -1,
            IsPrivate = isPrivate
        };

    [Theory]
    [InlineData(0.5)]
    [InlineData(6.9)]
    public void PrivateTorrent_IsNotPurgedBeforeSeedMinDays_EvenAtTheTargetRatio(double seededDays)
    {
        // A ratio of 25, far above the target, and even past the maximum age: the minimum still wins.
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 25, seededDays, completedDaysAgo: 40), Policy(minDays: 7, ratio: 1.0m, maxDays: 30), _now, released: false);

        Assert.Equal(SeedVerdict.Protected, evaluation.Verdict);
        Assert.False(evaluation.IsPurge);
        Assert.Equal(_now + TimeSpan.FromDays(7 - seededDays), evaluation.ProtectedUntil!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ReleasedTorrent_IsNotPurgedBeforeSeedMinDays()
    {
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 3, seededDays: 2, completedDaysAgo: 2), Policy(), _now, released: true);

        Assert.Equal(SeedVerdict.Protected, evaluation.Verdict);
    }

    [Fact]
    public void TargetRatio_PurgesOnceTheMinimumIsReached()
    {
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 1.0, seededDays: 7, completedDaysAgo: 7), Policy(), _now, released: false);

        Assert.Equal(SeedVerdict.PurgeTargetRatio, evaluation.Verdict);
    }

    [Fact]
    public void SeedMaxDays_Purges_EvenWhenTheRatioIsNeverReached()
    {
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 0.02, seededDays: 30, completedDaysAgo: 30), Policy(minDays: 7, ratio: 1.0m, maxDays: 30), _now, released: false);

        Assert.Equal(SeedVerdict.PurgeMaxAge, evaluation.Verdict);
    }

    [Fact]
    public void SeedMaxDays_Purges_WithoutAnyTargetRatio()
    {
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 0, seededDays: 31, completedDaysAgo: 31), Policy(ratio: null, maxDays: 30), _now, released: false);

        Assert.Equal(SeedVerdict.PurgeMaxAge, evaluation.Verdict);
    }

    [Fact]
    public void BetweenMinimumAndMaximum_BelowTheRatio_KeepsSeeding()
    {
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 0.4, seededDays: 10, completedDaysAgo: 10), Policy(), _now, released: false);

        Assert.Equal(SeedVerdict.Seeding, evaluation.Verdict);
    }

    [Fact]
    public void WithoutRatioNorMaximum_NothingIsEverPurged_ButReleasedTorrents()
    {
        var policy = Policy(ratio: null, maxDays: null);
        var old = Torrent(ratio: 50, seededDays: 400, completedDaysAgo: 400);

        Assert.Equal(SeedVerdict.Seeding, SeedEvaluator.Evaluate(old, policy, _now, released: false).Verdict);
        Assert.Equal(SeedVerdict.PurgeReleased, SeedEvaluator.Evaluate(old, policy, _now, released: true).Verdict);
    }

    [Fact]
    public void DownloadingTorrent_IsNeverPurged()
    {
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 5, seededDays: 0, completedDaysAgo: 0, progress: 0.4), Policy(minDays: 0, ratio: 0m, maxDays: 0), _now, released: true);

        Assert.Equal(SeedVerdict.Downloading, evaluation.Verdict);
    }

    [Fact]
    public void SeedingTime_IsWhatQBittorrentCounted_NotTheTimeSinceCompletion()
    {
        // Completed 20 days ago, but stopped most of the time: only 3 days seeded, which is what the tracker counts.
        var evaluation = SeedEvaluator.Evaluate(Torrent(ratio: 2, seededDays: 3, completedDaysAgo: 20), Policy(maxDays: 15), _now, released: false);

        Assert.Equal(SeedVerdict.Protected, evaluation.Verdict);
    }

    [Fact]
    public void SeedMinDays_DefaultsToFourteenDays()
    {
        var policy = SeedPolicy.From(ArrFixtures.Settings());

        Assert.Equal(TimeSpan.FromDays(SeedPolicy.DefaultMinDays), policy.MinSeedingTime);
        Assert.Equal(14, SeedPolicy.DefaultMinDays);
        Assert.Null(policy.TargetRatio);
        Assert.Null(policy.MaxAge);
        Assert.True(policy.Manages("radarr") && policy.Manages("sonarr") && policy.Manages("audiobooks"));
        Assert.False(policy.Manages("manual"));
    }

    [Fact]
    public async Task Cycle_DoesNotPurgeAPrivateTorrentBeforeSeedMinDays_EvenAtTheTargetRatio()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0), (SettingKeys.SeedMaxDays, 30));
        kit.AddTorrent("young", ratio: 12, seededDays: 2, completedDaysAgo: 45);

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Equal(SeedCycleStatus.Completed, result.Status);
        Assert.Empty(result.Purged);
        Assert.Equal(1, result.Protected);
        Assert.Empty(kit.Deletions);
    }

    [Fact]
    public async Task Cycle_PurgesAtTheMaximumAge_AndAtTheTargetRatio_WithTheirFiles()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0), (SettingKeys.SeedMaxDays, 30));
        kit.AddTorrent("ratio", ratio: 1.2, seededDays: 8);
        kit.AddTorrent("old", ratio: 0.1, seededDays: 31);
        kit.AddTorrent("seeding", ratio: 0.5, seededDays: 10);

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Equal(new[] { "old", "ratio" }, result.Purged.Select(p => p.Hash).Order());
        Assert.Equal(PurgeReason.MaxAge, result.Purged.Single(p => p.Hash == "old").Reason);
        Assert.Equal(PurgeReason.TargetRatio, result.Purged.Single(p => p.Hash == "ratio").Reason);
        var call = Assert.Single(kit.Server.Calls, c => c.Path == "torrents/delete");
        Assert.Contains("deleteFiles=true", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cycle_NeverTouchesATorrentOutsideTheManagedCategories()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0), (SettingKeys.SeedMaxDays, 30));
        kit.AddTorrent("mine", category: "manual", ratio: 9, seededDays: 400);

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Empty(result.Purged);
        Assert.Empty(kit.Deletions);
    }

    [Fact]
    public async Task Cycle_KeepsATorrentRadarrHasNotImportedYet()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedMinDays, 0), (SettingKeys.SeedTargetRatio, 1.0));
        kit.AddTorrent("pending-import", ratio: 3, seededDays: 1);
        kit.Server.Resource("queue/details").Add(new JsonObject { ["id"] = 1, ["movieId"] = 50, ["downloadId"] = "PENDING-IMPORT", ["trackedDownloadState"] = "importPending" });

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Empty(result.Purged);
        Assert.Equal(1, result.WaitingForImport);
    }

    [Fact]
    public async Task Cycle_KeepsEverything_WhenRadarrCannotBeReached()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        kit.AddTorrent("done", ratio: 3, seededDays: 20);
        kit.Server.Overrides.Insert(0, (_, path, _) => path.StartsWith("queue", StringComparison.Ordinal) ? new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.BadGateway) : null);

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Empty(result.Purged);
        Assert.Empty(kit.Deletions);
    }

    [Fact]
    public async Task Cycle_PurgesAnAudiobookOnlyOnceItsFilesAreHardLinkedIntoTheLibrary()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        kit.AddTorrent("linked", category: MediaLayout.AudiobooksCategory, ratio: 2, seededDays: 10, savePath: "/downloads/audiobooks");
        kit.AddTorrent("copied", category: MediaLayout.AudiobooksCategory, ratio: 2, seededDays: 10, savePath: "/downloads/audiobooks");
        kit.Files["linked"] = [new JsonObject { ["name"] = "Book/01.m4b", ["size"] = 10, ["priority"] = 1 }];
        kit.Files["copied"] = [new JsonObject { ["name"] = "Other/01.m4b", ["size"] = 10, ["priority"] = 1 }];
        kit.Links.Identities["/downloads/audiobooks/Book/01.m4b"] = new(1, 100, 2);
        kit.Links.Identities["/downloads/audiobooks/Other/01.m4b"] = new(1, 200, 1);

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Equal("linked", Assert.Single(result.Purged).Hash);
        Assert.Equal(1, result.WaitingForImport);
    }

    [Fact]
    public async Task Cycle_IsIdempotent_ARepeatedCycleNeitherDeletesNorRecordsTwice()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        kit.AddTorrent("ratio", ratio: 1.5, seededDays: 9);

        await kit.Runner.RunCycleAsync(CancellationToken.None);
        kit.RemoveTorrent("ratio"); // What qBittorrent did.
        var second = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Empty(second.Purged);
        Assert.Single(kit.Deletions);
        Assert.Single(kit.Store.GetRecentPurges());
    }

    [Fact]
    public async Task Schedule_SurvivesARestart_WithoutRunningTwiceInTheHour()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        Assert.NotNull(await kit.Runner.RunCycleIfDueAsync(CancellationToken.None));

        kit.Clock.Advance(TimeSpan.FromMinutes(10));
        kit.Restart();
        kit.AddTorrent("ratio", ratio: 1.5, seededDays: 9);

        Assert.Null(await kit.Runner.RunCycleIfDueAsync(CancellationToken.None));
        Assert.Empty(kit.Deletions);
        Assert.Equal(SeedingTestKit.Now + SeedPolicyRunner.CycleInterval, kit.Runner.NextCycleDueAt());
    }

    [Fact]
    public async Task Schedule_SurvivesARestart_WithoutSkippingACycleThatCameDueWhileDown()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        await kit.Runner.RunCycleIfDueAsync(CancellationToken.None);

        // Down for five hours: one catch up cycle at start, not five, and not none.
        kit.Clock.Advance(TimeSpan.FromHours(5));
        kit.Restart();
        kit.AddTorrent("ratio", ratio: 1.5, seededDays: 9);

        var result = await kit.Runner.RunCycleIfDueAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Purged);
        Assert.Null(await kit.Runner.RunCycleIfDueAsync(CancellationToken.None));
        Assert.Single(kit.Deletions);
    }

    [Fact]
    public async Task Schedule_ACycleCutByARestart_RunsAgainSoon_AndFinishesItsPurges()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        kit.AddTorrent("ratio", ratio: 1.5, seededDays: 9);
        kit.AddTorrent("gone", ratio: 1.5, seededDays: 9);

        // The process died after journaling two purges; qBittorrent had removed one of them.
        kit.Store.BeginCycle(SeedingTestKit.Now);
        kit.Store.BeginPurges([new PurgeRecord("ratio", "Dune", SeedingTestKit.Now, PurgeReason.TargetRatio), new PurgeRecord("gone", "Arrival", SeedingTestKit.Now, PurgeReason.TargetRatio)]);
        kit.RemoveTorrent("gone");
        kit.Clock.Advance(SeedPolicyRunner.RetryDelay);
        kit.Restart();

        var result = await kit.Runner.RunCycleIfDueAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new[] { "ratio" }, Assert.Single(kit.Deletions));
        Assert.Empty(kit.Store.GetPurgesInFlight());
        Assert.Equal(new[] { "gone", "ratio" }, kit.Store.GetRecentPurges().Select(p => p.Hash).Order());
    }

    [Fact]
    public async Task Cycle_WhenQBittorrentIsDown_PurgesNothing_AndRetriesWithinMinutes()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        kit.AddTorrent("ratio", ratio: 1.5, seededDays: 9);
        kit.QBittorrentDown = true;

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Equal(SeedCycleStatus.Failed, result.Status);
        Assert.Equal(SeedingTestKit.Now + SeedPolicyRunner.RetryDelay, kit.Runner.NextCycleDueAt());
    }

    [Fact]
    public async Task Cycle_ReleasesADeletedTitle_OnlyOnceItsMinimumIsReached()
    {
        using var kit = new SeedingTestKit();
        kit.AddTorrent("deleted", ratio: 0.1, seededDays: 3);
        kit.Store.AddPendingReleases([new PendingRelease("deleted", "Dune", true, SeedingTestKit.Now, Guid.NewGuid())]);

        var early = await kit.Runner.RunCycleAsync(CancellationToken.None);
        kit.Server.Resource("torrents/info")[0]!["seeding_time"] = (long)TimeSpan.FromDays(7).TotalSeconds;
        var late = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.Empty(early.Purged);
        var purge = Assert.Single(late.Purged);
        Assert.Equal(PurgeReason.Released, purge.Reason);
        Assert.Equal("Dune", purge.Title);
        Assert.Empty(kit.Store.GetPendingReleases());
    }

    [Fact]
    public async Task Service_RunsADueCycleAtStart()
    {
        using var kit = new SeedingTestKit();
        using var service = new SeedPolicyService(kit.Runner, kit.Clock, kit.Logs.CreateLogger<SeedPolicyService>());
        using var stop = new CancellationTokenSource();

        await service.StartAsync(stop.Token);
        for (var i = 0; i < 100 && kit.Store.LastCycleCompletedAt is null; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(SeedingTestKit.Now, kit.Store.LastCycleCompletedAt);
    }

    [Fact]
    public async Task NoLogNorResult_ContainsAPasskey()
    {
        using var kit = new SeedingTestKit((SettingKeys.SeedTargetRatio, 1.0));
        kit.AddTorrent("ratio", ratio: 1.5, seededDays: 9);
        kit.AddTorrent("young", ratio: 1.5, seededDays: 1);

        var result = await kit.Runner.RunCycleAsync(CancellationToken.None);

        Assert.DoesNotContain(SeedingTestKit.Passkey, System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.DoesNotContain(SeedingTestKit.Passkey, await System.IO.File.ReadAllTextAsync(kit.SeedingFile, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.All(kit.Logs.Entries, entry => Assert.DoesNotContain("passkey", entry, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TorrentModel_DoesNotReadTheTrackerNorTheMagnetLink()
    {
        var names = typeof(QBittorrentTorrent).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Tracker", StringComparison.OrdinalIgnoreCase) || n.Contains("Magnet", StringComparison.OrdinalIgnoreCase));
    }
}
