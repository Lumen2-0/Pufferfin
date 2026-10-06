using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Workflow;
using Pufferfin.Server.Tests.Audiobooks;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class AudiobookWorkflowTests : IDisposable
{
    private readonly AudiobookTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private Task<MediaRequest> Search(MediaRequest? request = null, RequestContext? context = null)
        => _kit.Workflow.SearchAsync(request ?? AudiobookTestKit.Request(), context ?? AudiobookTestKit.Context(), TestContext.Current.CancellationToken);

    private Task<MediaRequest> Track(MediaRequest request, RequestContext? context = null)
        => _kit.Workflow.TrackAsync(request, context ?? AudiobookTestKit.Context(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Search_QueuesTheSingleBook_NeverTheCompleteEdition()
    {
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune, l'Integrale (6 tomes) [FR]", seeders: 900, id: "complete"));
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", seeders: 4, id: "single"));

        var request = await Search();

        Assert.Equal(RequestState.Downloading, request.State);
        Assert.Equal("single", request.DownloadId);
        Assert.Equal("Frank Herbert - Dune Tome 1 [FR]", request.ReleaseTitle);
        Assert.Equal("fr", request.ChosenLanguage);
        var queued = Assert.Single(_kit.Queued);
        Assert.Equal("single", queued["source_id"]!.GetValue<string>());
        Assert.Equal("audiobook", queued["content_type"]!.GetValue<string>());

        // The search asks Shelfmark for audiobooks in the language of the request, with the key in a header.
        var search = Assert.Single(_kit.Searches);
        Assert.Contains("content_type=audiobook", search, StringComparison.Ordinal);
        Assert.Contains("languages=fr", search, StringComparison.Ordinal);
        Assert.Contains("title=Dune", search, StringComparison.Ordinal);
        Assert.All(_kit.Shelfmark.Calls, c => Assert.Equal(AudiobookTestKit.ShelfmarkKey, c.Request.Headers.GetValues("X-Api-Key").Single()));
    }

    [Fact]
    public async Task Search_OnlyCompleteEditions_QueuesNothing_AndSaysWhy()
    {
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune, l'Integrale (6 tomes) [FR]", seeders: 900));

        var request = await Search();

        Assert.Equal(RequestState.Failed, request.State);
        Assert.Equal(RequestProblemCode.OnlyCompleteEditions, request.Problem!.Code);
        Assert.Equal("Frank Herbert - Dune, l'Integrale (6 tomes) [FR]", request.Problem.Detail);
        Assert.Empty(_kit.Queued);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("downloading")]
    [InlineData("complete")]
    public async Task Search_ABookAlreadyInShelfmark_IsNeverQueuedTwice(string status)
    {
        // Queued by hand in Shelfmark, or downloaded already: the queue and the history both count.
        _kit.SetDownload(status, "by-hand", title: "Dune", progress: 40);
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]"));

        var request = await Search();

        Assert.Equal(RequestState.Downloading, request.State);
        Assert.Equal("by-hand", request.DownloadId);
        Assert.Empty(_kit.Queued);
        Assert.Empty(_kit.Searches);
    }

    [Fact]
    public async Task Search_ADifferentBookOfTheSeriesInShelfmark_DoesNotCount()
    {
        _kit.SetDownload("downloading", "other", title: "Le Messie de Dune Tome 2");
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", id: "single"));

        var request = await Search();

        Assert.Equal("single", request.DownloadId);
        Assert.Single(_kit.Queued);
    }

    [Fact]
    public async Task Search_AnotherBookCompleteInShelfmark_IsNotAdopted_NorImported()
    {
        // Reported in review: a "Dune Messiah" in the history was imported as "Dune".
        _kit.SetDownload("complete", "messiah", title: "Frank Herbert - Dune Messiah [FR]", progress: 100, downloadPath: _kit.CompleteTorrent("Dune Messiah"));
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", id: "single"));

        var request = await Search();

        Assert.Equal("single", request.DownloadId);
        Assert.Single(_kit.Queued);
        Assert.False(_kit.Disk.Library.Contains(RequestTestKit.FakeAudiobooks.Dune));
    }

    [Fact]
    public async Task Search_TheBookCompleteInShelfmarkInAnotherLanguage_IsNotAdopted()
    {
        // Reported in review: a "Dune [ENG]" in the history was adopted by a request in French.
        _kit.SetDownload("complete", "english", title: "Frank Herbert - Dune [ENG]", progress: 100);
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", id: "single"));

        var request = await Search(AudiobookTestKit.Request(language: "fr"));

        Assert.Equal("single", request.DownloadId);
        Assert.Single(_kit.Queued);
    }

    [Fact]
    public async Task Search_TheBookCompleteInShelfmarkInTheRightLanguage_IsAdopted()
    {
        _kit.SetDownload("complete", "french", title: "Frank Herbert - Dune [FR]", progress: 100);

        var request = await Search(AudiobookTestKit.Request(language: "fr"));

        Assert.Equal("french", request.DownloadId);
        Assert.Empty(_kit.Queued);
    }

    [Fact]
    public async Task Search_TheReleaseQueuedDuringTheSearch_IsNotQueuedAgain()
    {
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", id: "single"));
        _kit.Shelfmark.Overrides.Insert(0, (method, path, _) =>
        {
            // The search is slow: someone queues the same release meanwhile.
            if (method == HttpMethod.Get && path.StartsWith("releases?", StringComparison.Ordinal))
            {
                _kit.SetDownload("queued", "single", title: "Frank Herbert - Dune Tome 1 [FR]");
            }

            return null;
        });

        var request = await Search();

        Assert.Equal("single", request.DownloadId);
        Assert.Empty(_kit.Queued);
    }

    [Fact]
    public async Task Search_NoRelease_WaitsForSources()
    {
        var request = await Search();

        Assert.Equal(RequestState.WaitingForSources, request.State);
        Assert.Equal(RequestProblemCode.NoRelease, request.Problem!.Code);
        Assert.NotNull(request.NextAttemptAt);
    }

    [Fact]
    public async Task Search_SourcesUnreachable_WaitsWithTheReason_WithoutThePasskey()
    {
        _kit.SearchAnswer = () => FakeArrServer.Json(new JsonObject { ["error"] = "Prowlarr timed out on https://tracker.example/rss?passkey=SECRET-passkey-77" }, HttpStatusCode.ServiceUnavailable);

        var request = await Search();

        Assert.Equal(RequestState.WaitingForSources, request.State);
        Assert.Equal(RequestProblemCode.IndexersUnavailable, request.Problem!.Code);
        Assert.Contains("Prowlarr timed out", request.Problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", request.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_ShelfmarkNotConfigured_FailsCleanly_WithoutCallingIt()
    {
        var request = await Search(context: AudiobookTestKit.Context(shelfmark: false));

        Assert.Equal(RequestState.Failed, request.State);
        Assert.Equal(RequestProblemCode.ServiceDisabled, request.Problem!.Code);
        Assert.Empty(_kit.Shelfmark.Calls);
    }

    [Fact]
    public async Task Search_ABookAlreadyInTheLibrary_IsAvailable()
    {
        _kit.Disk.Download("Dune.m4b");
        await _kit.Disk.Library.ImportAsync(Path.Combine(_kit.Disk.Downloads, "Dune.m4b"), RequestTestKit.FakeAudiobooks.Dune, TestContext.Current.CancellationToken);

        var request = await Search();

        Assert.Equal(RequestState.Available, request.State);
        Assert.Empty(_kit.Shelfmark.Calls);
    }

    [Fact]
    public async Task Track_ReportsTheProgress()
    {
        _kit.SetDownload("downloading", "single", progress: 42.5);

        var request = await Track(AudiobookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "single" });

        Assert.Equal(RequestState.Downloading, request.State);
        Assert.Equal(0.425, request.Progress!.Fraction);
    }

    [Fact]
    public async Task Track_AFailedDownload_SearchesAnotherRelease_WithoutThePasskey()
    {
        _kit.SetDownload("error", "single", message: "Torrent rejected: https://tracker.example/dl?passkey=SECRET-passkey-77");

        var request = await Track(AudiobookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "single", Attempts = 1 });

        Assert.Equal(RequestState.Searching, request.State);
        Assert.Equal(RequestProblemCode.DownloadFailed, request.Problem!.Code);
        Assert.DoesNotContain("SECRET", request.Problem.Detail, StringComparison.Ordinal);
        Assert.Null(request.DownloadId);
        Assert.Equal(["single"], request.ExcludedReleases);
    }

    [Fact]
    public async Task Track_ACompleteDownload_IsFiledAsHardLinks_WithTheCover_AndLinkedToItsTorrentByInode()
    {
        var download = _kit.CompleteTorrent();
        _kit.SetDownload("complete", "single", progress: 100, downloadPath: download);

        var request = await Track(AudiobookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "single" });

        Assert.Equal(RequestState.Available, request.State);
        Assert.Null(request.Problem);
        var folder = Path.Combine(_kit.Disk.LibraryRoot, "Frank Herbert", "Dune");
        Assert.Equal(folder, request.LibraryPath);
        Assert.Equal(FileIdentity.Of(Path.Combine(download, "Dune - Part 1.m4b")), FileIdentity.Of(Path.Combine(folder, "Dune - Part 1.m4b")));
        Assert.True(File.Exists(Path.Combine(folder, AudiobookLibrary.CoverFileName)));
        Assert.Equal(AudiobookTestKit.TorrentHash, request.TorrentHash);
        Assert.Contains(folder, _kit.Library.Refreshed);
    }

    [Fact]
    public async Task Track_AnImportThatCannotBeDone_IsBlocked_WithTheReason()
    {
        _kit.SetDownload("complete", "single", progress: 100, downloadPath: Path.Combine(_kit.Disk.Root, "elsewhere"));

        var request = await Track(AudiobookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "single" });

        Assert.Equal(RequestState.Failed, request.State);
        Assert.Equal(RequestProblemCode.ImportBlocked, request.Problem!.Code);
        Assert.Contains("not visible from Pufferfin", request.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Track_FilesPufferfinCannotChange_AreReportedOnTheRequest()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.UserName == "root", "root can write anything.");
        var download = _kit.CompleteTorrent();
        foreach (var file in Directory.GetFiles(download))
        {
            // Hard links share their permissions: a file qBittorrent made read only is read only in the library too.
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(file, FileAttributes.ReadOnly);
            }
            else
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead);
            }
        }

        _kit.SetDownload("complete", "single", progress: 100, downloadPath: download);

        var request = await Track(AudiobookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "single" });

        Assert.Equal(RequestState.Available, request.State);
        Assert.Equal(RequestProblemCode.FilesNotWritable, request.Problem!.Code);
    }

    [Fact]
    public async Task Track_ShelfmarkUnreachable_KeepsTheRequest()
    {
        _kit.Shelfmark.Overrides.Insert(0, (_, _, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var downloading = AudiobookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "single" };

        var request = await Track(downloading);

        Assert.Equal(RequestState.Downloading, request.State);
        Assert.Equal(RequestProblemCode.ServiceUnavailable, request.Problem!.Code);
    }

    [Fact]
    public async Task NoLog_CarriesTheKeyOrAPasskey()
    {
        _kit.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", id: "single"));
        var request = await Search();
        _kit.SetDownload("error", "single", message: "Bad torrent https://tracker.example/dl?passkey=SECRET-passkey-77");
        await Track(request);

        Assert.All(_kit.Logs.Entries, e => Assert.DoesNotContain("SECRET", e, StringComparison.Ordinal));
    }
}
