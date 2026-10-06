using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Selection;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class SelectionTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly List<ArrEpisode> _episodes =
    [
        new() { Id = 11, SeasonNumber = 1, EpisodeNumber = 1, AirDate = "2024-01-10" },
        new() { Id = 12, SeasonNumber = 1, EpisodeNumber = 2, AirDate = "2024-01-17" },
        new() { Id = 21, SeasonNumber = 2, EpisodeNumber = 1, AirDate = "2025-03-02" },
    ];

    private static ArrLanguage French => new() { Id = 2, Name = "French" };

    private static ArrLanguage English => new() { Id = 1, Name = "English" };

    private static ArrLanguage German => new() { Id = 4, Name = "German" };

    private static ArrRelease Release(string title, params ArrLanguage[] languages)
        => new() { Guid = title, Title = title, IndexerId = 1, Languages = languages, Protocol = "torrent", Seeders = 10 };

    private static ArrRelease Episode(string title, int season, int episode, params ArrLanguage[] languages)
    {
        var release = Release(title, languages);
        release.SeasonNumber = season;
        release.EpisodeNumbers = [episode];
        return release;
    }

    private static ArrRelease Pack(string title, int season, params ArrLanguage[] languages)
    {
        var release = Release(title, languages);
        release.SeasonNumber = season;
        release.FullSeason = true;
        return release;
    }

    private static ArrHistoryRecord History(string eventType, int minutes, string? downloadId = "HASH1")
        => new() { EventType = eventType, Date = _t0.AddMinutes(minutes), DownloadId = downloadId };

    // Anti double download: queue AND history.
    [Fact]
    public void DuplicateGuard_AGrabInTheHistoryButNotYetInTheQueue_Covers()
    {
        var coverage = DuplicateGuard.Check(false, [], [History("grabbed", 0)]);

        Assert.True(coverage.Covered);
        Assert.Equal("GrabbedInHistory", coverage.Reason);
        Assert.Equal(["HASH1"], coverage.DownloadIds);
    }

    [Fact]
    public void DuplicateGuard_AnItemInTheQueue_Covers()
    {
        var coverage = DuplicateGuard.Check(false, [new ArrQueueItem { DownloadId = "HASH2", TrackedDownloadState = "downloading" }], []);

        Assert.True(coverage.Covered);
        Assert.Equal("InQueue", coverage.Reason);
    }

    [Fact]
    public void DuplicateGuard_AFileOnDisk_Covers()
        => Assert.True(DuplicateGuard.Check(true, [], []).Covered);

    [Theory]
    [InlineData("downloadFailed")]
    [InlineData("downloadIgnored")]
    [InlineData("movieFileDeleted")]
    [InlineData("episodeFileDeleted")]
    public void DuplicateGuard_AGrabUndoneLater_NoLongerCovers(string laterEvent)
        => Assert.False(DuplicateGuard.Check(false, [], [History("grabbed", 0), History("downloadFolderImported", 5), History(laterEvent, 10)]).Covered);

    [Fact]
    public void DuplicateGuard_AFailureOfAnOlderGrab_DoesNotUndoTheNewOne()
    {
        var history = new[] { History("grabbed", 0, "OLD"), History("downloadFailed", 5, "OLD"), History("grabbed", 10, "NEW") };

        Assert.True(DuplicateGuard.Check(false, [], history).Covered);
    }

    [Fact]
    public void DuplicateGuard_AFailedQueueItem_DoesNotCover()
        => Assert.False(DuplicateGuard.Check(false, [new ArrQueueItem { DownloadId = "X", TrackedDownloadState = "failed" }], []).Covered);

    [Fact]
    public void DuplicateGuard_NothingAnywhere_DoesNotCover()
        => Assert.False(DuplicateGuard.Check(false, [], [History("downloadFolderImported", 0)]).Covered);

    // Episode matching.
    [Fact]
    public void EpisodeMatcher_MatchesByNumberFirst()
        => Assert.Equal(12, EpisodeMatcher.Match(_episodes, 1, 2, new DateOnly(2025, 3, 2))!.Id);

    [Theory]
    [InlineData(2025, 3, 2)]
    [InlineData(2025, 3, 1)]
    [InlineData(2025, 3, 3)]
    public void EpisodeMatcher_FallsBackToTheAirDate_OneDayEitherSide(int year, int month, int day)
        => Assert.Equal(21, EpisodeMatcher.Match(_episodes, 3, 1, new DateOnly(year, month, day))!.Id);

    [Fact]
    public void EpisodeMatcher_TwoDaysOff_IsNoMatch()
        => Assert.Null(EpisodeMatcher.Match(_episodes, 3, 1, new DateOnly(2025, 3, 4)));

    [Fact]
    public void EpisodeMatcher_WithoutNumberNorDate_IsNoMatch()
        => Assert.Null(EpisodeMatcher.Match(_episodes, 3, 1, null));

    [Fact]
    public void EpisodeMatcher_UsesTheUtcAirDate_WhenTheLocalOneIsMissing()
    {
        List<ArrEpisode> episodes = [new() { Id = 31, SeasonNumber = 1, EpisodeNumber = 5, AirDateUtc = new DateTimeOffset(2024, 5, 5, 23, 0, 0, TimeSpan.Zero) }];

        Assert.Equal(31, EpisodeMatcher.Match(episodes, 4, 9, new DateOnly(2024, 5, 6))!.Id);
    }

    // Language plan and fallback.
    [Fact]
    public void LanguagePlan_TriesTheRequestedLanguage_ThenThePreferredOnes_ThenTheOriginal()
    {
        var plan = LanguagePlan.Build("de", ["fr", "de", "en"]);

        Assert.Equal(["de", "fr", "en", MediaRequest.OriginalLanguage], plan.Select(c => c.Code));
    }

    [Fact]
    public void LanguagePlan_ForTheOriginalVersion_OnlyTriesTheOriginal()
        => Assert.Equal([MediaRequest.OriginalLanguage], LanguagePlan.Build(MediaRequest.OriginalLanguage, ["fr"]).Select(c => c.Code));

    [Fact]
    public void Selector_TakesThePreferredLanguageFirst()
    {
        var releases = new[] { Release("Film.2024.1080p.ENG", English), Release("Film.2024.MULTi.VFF.1080p", French, English) };

        var selection = ReleaseSelector.Select(releases, LanguagePlan.Build("fr", []), "en", true, SelectionScope.Movie);

        Assert.Equal("Film.2024.MULTi.VFF.1080p", selection.Release!.Title);
        Assert.Equal("fr", selection.Language!.Code);
    }

    [Fact]
    public void Selector_FallsBackToTheOriginalVersion()
    {
        var releases = new[] { Release("Film.2024.GERMAN.1080p", German), Release("Film.2024.1080p.ENG", English) };

        var selection = ReleaseSelector.Select(releases, LanguagePlan.Build("fr", []), "en", true, SelectionScope.Movie);

        Assert.Equal("Film.2024.1080p.ENG", selection.Release!.Title);
        Assert.True(selection.Language!.IsOriginal);
    }

    [Fact]
    public void Selector_WithoutPreferredNorOriginal_FailsWithAnExplicitReason()
    {
        var selection = ReleaseSelector.Select([Release("Film.2024.GERMAN.1080p", German)], LanguagePlan.Build("fr", []), "en", true, SelectionScope.Movie);

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoAcceptableLanguage, selection.Problem!.Code);
        Assert.Contains("French", selection.Problem.Message, StringComparison.Ordinal);
        Assert.Contains("original version", selection.Problem.Message, StringComparison.Ordinal);
        Assert.Contains("German", selection.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Selector_WithoutAnyRelease_ReportsNoRelease()
        => Assert.Equal(RequestProblemCode.NoRelease, ReleaseSelector.Select([], LanguagePlan.Build("fr", []), "en", true, SelectionScope.Movie).Problem!.Code);

    // Subtitled only releases.
    [Fact]
    public void Selector_RefusesASubtitledOnlyRelease_WhenTheAudioIsWantedInThatLanguage()
    {
        // Radarr parses VOSTFR as French: the title is what tells the truth.
        var releases = new[] { Release("Film.2024.VOSTFR.1080p", French), Release("Film.2024.1080p.ENG", English) };

        var selection = ReleaseSelector.Select(releases, LanguagePlan.Build("fr", []), "en", true, SelectionScope.Movie);

        Assert.Equal("Film.2024.1080p.ENG", selection.Release!.Title);
        Assert.True(selection.Language!.IsOriginal);
    }

    [Fact]
    public void Selector_AcceptsASubtitledRelease_WhenTheRefusalIsOff()
    {
        var selection = ReleaseSelector.Select([Release("Film.2024.VOSTFR.1080p", French)], LanguagePlan.Build("fr", []), "en", false, SelectionScope.Movie);

        Assert.Equal("fr", selection.Language!.Code);
    }

    [Fact]
    public void Selector_KeepsASubtitledRelease_OfAFilmWhoseOriginalLanguageItIs()
    {
        var selection = ReleaseSelector.Select([Release("Film.2024.FRENCH.STFR.1080p", French)], LanguagePlan.Build("fr", []), "fr", true, SelectionScope.Movie);

        Assert.Equal("fr", selection.Language!.Code);
    }

    // A single episode never comes from a season pack.
    [Fact]
    public void Selector_ForOneEpisode_SkipsSeasonPacks_EvenWhenRankedFirst()
    {
        var releases = new[] { Pack("Show.S01.MULTi.1080p", 1, French), Episode("Show.S01E02.MULTi.1080p", 1, 2, French) };

        var selection = ReleaseSelector.Select(releases, LanguagePlan.Build("fr", []), "en", true, new SelectionScope(SelectionMode.Episode, 1, 2));

        Assert.Equal("Show.S01E02.MULTi.1080p", selection.Release!.Title);
    }

    [Fact]
    public void Selector_ForOneEpisode_WithOnlySeasonPacks_TakesNothing()
    {
        var releases = new[] { Pack("Show.S01.MULTi.1080p", 1, French), Pack("Show.S01.1080p", 1, English) };

        var selection = ReleaseSelector.Select(releases, LanguagePlan.Build("fr", []), "en", true, new SelectionScope(SelectionMode.Episode, 1, 2));

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.OnlySeasonPacks, selection.Problem!.Code);
    }

    [Fact]
    public void Selector_ForOneEpisode_WithOnlyAMultiEpisodeRange_TakesNothing()
    {
        // S01E01-E10 contains S01E03, but it is the whole season under another name.
        var range = Release("Show.S01E01-E10.1080p", English);
        range.SeasonNumber = 1;
        range.EpisodeNumbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        var selection = ReleaseSelector.Select([range], LanguagePlan.Build("en", []), "en", true, new SelectionScope(SelectionMode.Episode, 1, 3));

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.OnlySeasonPacks, selection.Problem!.Code);
    }

    [Fact]
    public void Selector_ForOneEpisode_SkipsOtherEpisodesAndBundles_AndTakesTheEpisodeAlone()
    {
        var other = Episode("Show.S01E03.1080p", 1, 3, English);
        var bundle = Release("Show.S01E02E03.1080p", English);
        bundle.SeasonNumber = 1;
        bundle.EpisodeNumbers = [2, 3];
        var alone = Episode("Show.S01E02.720p", 1, 2, English);

        var selection = ReleaseSelector.Select([other, bundle, alone], LanguagePlan.Build("en", []), "en", true, new SelectionScope(SelectionMode.Episode, 1, 2));

        Assert.Equal("Show.S01E02.720p", selection.Release!.Title);
    }

    [Fact]
    public void Selector_ForASeason_OnlyTakesAPackOfThatSeason()
    {
        var releases = new[] { Episode("Show.S02E01.1080p", 2, 1, English), Pack("Show.S01.1080p", 1, English), Pack("Show.S02.1080p", 2, English) };

        var selection = ReleaseSelector.Select(releases, LanguagePlan.Build("en", []), "en", true, new SelectionScope(SelectionMode.SeasonPack, 2));

        Assert.Equal("Show.S02.1080p", selection.Release!.Title);
    }

    [Fact]
    public void Selector_SkipsReleasesRejectedForAnotherReasonThanLanguage_AndDeadTorrents()
    {
        var blocklisted = Release("Film.2024.MULTi.1080p.A", French);
        blocklisted.Rejected = true;
        blocklisted.Rejections = ["Release is blocklisted"];
        var dead = Release("Film.2024.MULTi.1080p.B", French);
        dead.Seeders = 0;
        var otherLanguageProfile = Release("Film.2024.MULTi.1080p.C", French);
        otherLanguageProfile.Rejected = true;
        otherLanguageProfile.Rejections = ["Custom Formats Pufferfin audio English have score 0 below Movie's profile minimum 100"];

        var selection = ReleaseSelector.Select([blocklisted, dead, otherLanguageProfile], LanguagePlan.Build("fr", []), "en", true, SelectionScope.Movie);

        Assert.Equal("Film.2024.MULTi.1080p.C", selection.Release!.Title);
    }

    // Every failure says why.
    [Fact]
    public void AFailedRequest_AlwaysCarriesAReason()
    {
        var request = new MediaRequest
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Kind = RequestKind.Movie,
            TmdbId = 1,
            Title = "Film",
            Language = "fr",
            State = RequestState.Searching,
            CreatedAt = _t0,
            UpdatedAt = _t0
        };

        Assert.Throws<ArgumentException>(() => request.To(RequestState.Failed, _t0));
        var failed = request.Fail(_t0, RequestProblemCode.NoIndexer, "Radarr has no indexer able to search.");
        Assert.False(string.IsNullOrWhiteSpace(failed.Problem!.Message));
    }
}
