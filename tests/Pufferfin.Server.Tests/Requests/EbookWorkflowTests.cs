using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Ebooks;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Workflow;
using Pufferfin.Server.Tests.Audiobooks;
using Pufferfin.Server.Tests.Ebooks;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class EbookWorkflowTests : IDisposable
{
    private readonly EbookTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private RequestContext Context(bool shelfmark = true) => AudiobookTestKit.Context(shelfmark: shelfmark, disk: _kit.Disk);

    private Task<MediaRequest> Search(MediaRequest? request = null, RequestContext? context = null)
        => _kit.Workflow.SearchAsync(request ?? EbookTestKit.Request(), context ?? Context(), TestContext.Current.CancellationToken);

    private Task<MediaRequest> Track(MediaRequest request, RequestContext? context = null)
        => _kit.Workflow.TrackAsync(request, context ?? Context(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Search_QueuesTheBestEbook_AsAnEbook_ThroughTheMetadataProvider()
    {
        _kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune Omnibus (Books 1-3) [FR]", seeders: 900, id: "omnibus"));
        _kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune T1 [FR]", seeders: 40, format: "pdf", id: "pdf"));
        _kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune T1 [FR]", seeders: 4, format: "epub", id: "epub"));

        var request = await Search();

        Assert.Equal(RequestState.Downloading, request.State);
        Assert.Equal("epub", request.DownloadId);
        Assert.Equal("fr", request.ChosenLanguage);
        var queued = Assert.Single(_kit.Queued);
        Assert.Equal("epub", queued["source_id"]!.GetValue<string>());
        Assert.Equal("ebook", queued["content_type"]!.GetValue<string>());

        // The releases of the book as its metadata provider knows it, ebooks only, in the language of the request.
        var search = Assert.Single(_kit.Searches);
        Assert.Contains("content_type=ebook", search, StringComparison.Ordinal);
        Assert.Contains("provider=hardcover", search, StringComparison.Ordinal);
        Assert.Contains("book_id=312460", search, StringComparison.Ordinal);
        Assert.Contains("languages=fr", search, StringComparison.Ordinal);
        Assert.All(_kit.Shelfmark.Calls, c => Assert.Equal(AudiobookTestKit.ShelfmarkKey, c.Request.Headers.GetValues("X-Api-Key").Single()));
    }

    [Fact]
    public async Task Search_OnlyAnOmnibus_QueuesNothing_AndSaysWhy()
    {
        _kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune Omnibus (Books 1-3) [FR]", seeders: 900));

        var request = await Search();

        Assert.Equal(RequestState.Failed, request.State);
        Assert.Equal(RequestProblemCode.OnlyCompleteEditions, request.Problem!.Code);
        Assert.Empty(_kit.Queued);
    }

    [Fact]
    public async Task Search_AnEbookDownloadOfTheBookInShelfmark_IsAdopted_NeverQueuedTwice()
    {
        _kit.SetDownload("downloading", "by-hand", "Frank Herbert - Dune T1 [FR]", progress: 30);
        _kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune T1 [FR]", id: "other"));

        var request = await Search();

        Assert.Equal(RequestState.Downloading, request.State);
        Assert.Equal("by-hand", request.DownloadId);
        Assert.Empty(_kit.Queued);
        Assert.Empty(_kit.Searches);
    }

    [Fact]
    public async Task Search_TheAudiobookOfTheSameBook_IsNotTakenForTheEbook_NorTheOtherWayRound()
    {
        _kit.SetDownload("downloading", "audio", "Frank Herbert - Dune T1 [FR]", contentType: "audiobook");
        _kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune T1 [FR]", id: "ebook"));

        var request = await Search();

        Assert.Equal("ebook", request.DownloadId);
        Assert.Single(_kit.Queued);

        // The audiobook workflow, over the same Shelfmark queue, leaves the ebook download alone.
        using var audiobooks = new AudiobookTestKit();
        audiobooks.SetDownload("downloading", "ebook", "Frank Herbert - Dune Tome 1 [FR]");
        audiobooks.Status["downloading"]!["ebook"]!["content_type"] = "ebook";
        audiobooks.Releases.Add(AudiobookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", id: "audio"));
        var audiobook = await audiobooks.Workflow.SearchAsync(AudiobookTestKit.Request(), AudiobookTestKit.Context(), TestContext.Current.CancellationToken);
        Assert.Equal("audio", audiobook.DownloadId);
    }

    [Fact]
    public async Task Search_ShelfmarkNotConfigured_FailsClearly()
    {
        var request = await Search(context: Context(shelfmark: false));

        Assert.Equal(RequestState.Failed, request.State);
        Assert.Equal(RequestProblemCode.ServiceDisabled, request.Problem!.Code);
        Assert.Empty(_kit.Shelfmark.Calls);
    }

    [Fact]
    public async Task Search_UnknownBook_FailsAsUnknownTitle()
    {
        var request = await Search(EbookTestKit.Request() with { BookId = "999" });

        Assert.Equal(RequestState.Failed, request.State);
        Assert.Equal(RequestProblemCode.TitleUnknown, request.Problem!.Code);
    }

    [Fact]
    public async Task Track_ReportsTheProgressOfTheShelfmarkDownload()
    {
        _kit.SetDownload("downloading", "epub", progress: 42.5);

        var request = await Track(EbookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "epub" });

        Assert.Equal(RequestState.Downloading, request.State);
        Assert.Equal(0.425, request.Progress!.Fraction);
    }

    [Fact]
    public async Task Track_ACompleteDownload_IsFiledAsAHardLink_WithItsCover_AndItsTorrentFoundByInode()
    {
        _kit.Disk.RequireLinks();
        var download = _kit.CompleteTorrent();
        _kit.SetDownload("complete", "epub", downloadPath: download);

        var request = await Track(EbookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "epub" });

        Assert.Equal(RequestState.Available, request.State);
        var folder = Path.Combine(_kit.LibraryRoot, "Frank Herbert", "Dune");
        Assert.Equal(folder, request.LibraryPath);

        // One book file only, the best format, so Jellyfin takes the folder for one book.
        var filed = Assert.Single(Directory.EnumerateFiles(folder), EbookFormats.IsBook);
        Assert.Equal(Path.Combine(folder, "Dune.epub"), filed);
        Assert.True(_kit.Disk.SameFile(filed, Path.Combine(download, "Dune.epub")));

        // The cover of the provider, read back from the Shelfmark cover cache path.
        Assert.Equal(AudiobookDisk.CoverBytes, await File.ReadAllBytesAsync(Path.Combine(folder, AudiobookLibrary.CoverFileName), TestContext.Current.CancellationToken));
        Assert.Equal("https://assets.hardcover.app/dune.jpg", _kit.Disk.Covers.Requests.Single().RequestUri!.AbsoluteUri);

        // No *arr history for books: the inode leads to the torrent, among the others of the category.
        Assert.Equal(EbookTestKit.TorrentHash, request.TorrentHash);
        Assert.Equal(folder, Assert.Single(_kit.Media.Refreshed));
    }

    [Fact]
    public async Task Track_AZipRelease_IsExtracted_AndItsTorrentFoundByTheInodeOfTheArchive()
    {
        _kit.Disk.RequireLinks();
        var archive = _kit.CompleteZipTorrent();
        _kit.SetDownload("complete", "zip", downloadPath: archive);

        var request = await Track(EbookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "zip" });

        Assert.Equal(RequestState.Available, request.State);
        var filed = Path.Combine(_kit.LibraryRoot, "Frank Herbert", "Dune", "Dune.epub");
        Assert.Equal("the epub of the book", await File.ReadAllTextAsync(filed, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(filed)!), EbookFormats.IsBook);
        Assert.Equal(EbookTestKit.TorrentHash, request.TorrentHash);

        // Imported again: nothing changes, nothing breaks.
        var again = await Track(EbookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "zip" });
        Assert.Equal(RequestState.Available, again.State);
    }

    [Fact]
    public async Task Track_ARarRelease_IsNotOpened_AndTheImportSaysWhy()
    {
        _kit.Disk.RequireLinks();
        var archive = _kit.Disk.Download("Frank Herbert - Dune [FR].rar", "rar");
        _kit.SetDownload("complete", "rar", downloadPath: archive);

        var request = await Track(EbookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "rar" });

        Assert.Equal(RequestState.Failed, request.State);
        Assert.Equal(RequestProblemCode.ImportBlocked, request.Problem!.Code);
        Assert.Contains("RAR", request.Problem.Detail, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_kit.LibraryRoot, "Frank Herbert", "Dune")) && Directory.EnumerateFiles(Path.Combine(_kit.LibraryRoot, "Frank Herbert", "Dune")).Any(EbookFormats.IsBook));
    }

    [Fact]
    public async Task Track_ADownloadWithNoReadableFormat_IsNotFiled_AndTheImportNamesTheFormats()
    {
        _kit.Disk.RequireLinks();
        var folder = Path.GetDirectoryName(_kit.Disk.Download(Path.Combine("Dune mobi", "Dune.mobi"), "mobi"))!;
        _kit.SetDownload("complete", "mobi", downloadPath: folder);

        var request = await Track(EbookTestKit.Request(state: RequestState.Downloading) with { DownloadId = "mobi" });

        Assert.Equal(RequestProblemCode.ImportBlocked, request.Problem!.Code);
        Assert.Contains("mobi", request.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoKey_NorPasskey_ReachesTheLogs_OrTheRequest()
    {
        _kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune T1 [FR]"));
        var request = await Search();
        _kit.SetDownload("error", request.DownloadId!);
        _kit.Status["error"]![request.DownloadId!]!["status_message"] = "Tracker refused https://tracker.example/dl?passkey=SECRET-passkey-77 with key " + AudiobookTestKit.ShelfmarkKey;

        var failed = await Track(request with { Attempts = 1 });

        Assert.Equal(RequestProblemCode.DownloadFailed, failed.Problem!.Code);
        Assert.DoesNotContain("SECRET", failed.Problem.Detail, StringComparison.Ordinal);
        Assert.All(_kit.Logs.Entries, e => Assert.DoesNotContain("SECRET", e, StringComparison.Ordinal));
    }
}
