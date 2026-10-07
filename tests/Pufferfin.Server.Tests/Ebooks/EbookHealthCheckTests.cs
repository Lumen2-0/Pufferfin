using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;
using Pufferfin.Server.Ebooks;
using Pufferfin.Server.Storage;
using Pufferfin.Server.Tests.Audiobooks;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Ebooks;

public sealed class EbookHealthCheckTests : IDisposable
{
    private readonly AudiobookDisk _disk = new();
    private readonly FakeArrServer _qBittorrent = new();
    private readonly FakeTester _tester = new();
    private readonly EbookTestKit.FakeLibraryFolders _libraries = new();

    public EbookHealthCheckTests()
    {
        _qBittorrent.Objects["torrents/categories"] = new JsonObject
        {
            ["audiobooks"] = new JsonObject { ["name"] = "audiobooks", ["savePath"] = _disk.Downloads },
            ["books"] = new JsonObject { ["name"] = "books", ["savePath"] = _disk.Downloads }
        };
        _libraries.Folders.Add(Path.Combine(_disk.Media, EbookLibrary.FolderName));
    }

    private EbookHealthCheck Check => new(
        _tester,
        new QBittorrentClient(_qBittorrent, NullLogger<QBittorrentClient>.Instance, TimeSpan.FromSeconds(5)),
        new EbookLibrary(_disk.Covers, _disk.Links, () => Path.Combine(_disk.Media, EbookLibrary.FolderName), NullLogger<EbookLibrary>.Instance),
        _libraries,
        new HardlinkCheck(_disk.Links, TimeProvider.System, NullLogger<HardlinkCheck>.Instance),
        TimeProvider.System);

    public void Dispose()
    {
        _qBittorrent.Dispose();
        _disk.Dispose();
    }

    private Task<EbookHealthReport> Run(bool shelfmark = true, bool qBittorrent = true)
        => Check.RunAsync(AudiobookTestKit.Context(shelfmark, qBittorrent, disk: _disk).Settings, TestContext.Current.CancellationToken);

    [Fact]
    public async Task WithoutShelfmark_BooksAreOff_AndNothingIsChecked_NorIsItAnError()
    {
        var report = await Run(shelfmark: false);

        Assert.False(report.Enabled);
        Assert.Empty(report.Checks);
        Assert.False(report.HasProblems);
        Assert.Empty(_qBittorrent.Calls);
    }

    [Fact]
    public async Task AReadySetup_HasNoProblem()
    {
        _disk.RequireLinks();
        var report = await Run();

        Assert.True(report.Enabled);
        Assert.All(report.Checks, c => Assert.Equal(AudiobookCheckStatus.Ok, c.Status));
        Assert.Equal(Enum.GetValues<EbookCheckCode>().Length, report.Checks.Count);
    }

    [Fact]
    public async Task AMissingBooksCategory_IsAProblem_ThatSaysWhatToSet()
    {
        // The audiobooks category alone does not do: the two kinds never share one.
        _qBittorrent.Objects["torrents/categories"] = JsonNode.Parse("""{"audiobooks":{"name":"audiobooks","savePath":""}}""")!;

        var check = (await Run()).Checks.Single(c => c.Code == EbookCheckCode.QBittorrentCategory);

        Assert.Equal(AudiobookCheckStatus.Problem, check.Status);
        Assert.Contains("QBITTORRENT_CATEGORY to books", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedShelfmarkKey_IsAProblem_WithoutTheKey()
    {
        _tester.Status = ConnectionStatus.KeyRejected;

        var report = await Run();
        var check = report.Checks.Single(c => c.Code == EbookCheckCode.ShelfmarkConnection);

        Assert.Equal(AudiobookCheckStatus.Problem, check.Status);
        Assert.Contains("SHELFMARK_API_KEY", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AudiobookTestKit.ShelfmarkKey, System.Text.Json.JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoJellyfinBooksLibraryOverTheFolder_IsAProblem()
    {
        _libraries.Folders.Clear();
        _libraries.Folders.Add(Path.Combine(_disk.Media, "movies"));

        var check = (await Run()).Checks.Single(c => c.Code == EbookCheckCode.JellyfinLibrary);

        Assert.Equal(AudiobookCheckStatus.Problem, check.Status);
    }

    [Fact]
    public async Task ABooksLibraryOverTheWholeMediaFolder_IndexesTheBooksToo()
    {
        _libraries.Folders.Clear();
        _libraries.Folders.Add(_disk.Media);

        var check = (await Run()).Checks.Single(c => c.Code == EbookCheckCode.JellyfinLibrary);

        Assert.Equal(AudiobookCheckStatus.Ok, check.Status);
    }

    private sealed class FakeTester : IServiceConnectionTester
    {
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Connected;

        public Task<ConnectionTestResult> TestAsync(PufferfinService service, PufferfinSettings settings, CancellationToken cancellationToken)
            => Task.FromResult(new ConnectionTestResult(service, Status, null, TimeSpan.Zero));
    }
}
