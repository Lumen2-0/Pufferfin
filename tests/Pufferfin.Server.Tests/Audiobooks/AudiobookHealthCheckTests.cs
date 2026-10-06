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
using Pufferfin.Server.Tests.Requests;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Audiobooks;

public sealed class AudiobookHealthCheckTests : IDisposable
{
    private readonly AudiobookDisk _disk = new();
    private readonly FakeArrServer _qBittorrent = new();
    private readonly FakeTester _tester = new();

    public AudiobookHealthCheckTests()
    {
        _qBittorrent.Objects["torrents/categories"] = new JsonObject
        {
            ["audiobooks"] = new JsonObject { ["name"] = "audiobooks", ["savePath"] = _disk.Downloads }
        };
    }

    private AudiobookHealthCheck Check => new(_tester, new QBittorrentClient(_qBittorrent, NullLogger<QBittorrentClient>.Instance, TimeSpan.FromSeconds(5)), _disk.Library, TimeProvider.System);

    public void Dispose()
    {
        _qBittorrent.Dispose();
        _disk.Dispose();
    }

    private Task<AudiobookHealthReport> Run(bool shelfmark = true, bool qBittorrent = true)
        => Check.RunAsync(AudiobookTestKit.Context(shelfmark, qBittorrent).Settings, TestContext.Current.CancellationToken);

    [Fact]
    public async Task WithoutShelfmark_AudiobooksAreOff_AndNothingIsChecked()
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
        var report = await Run();

        Assert.True(report.Enabled);
        Assert.All(report.Checks, c => Assert.Equal(AudiobookCheckStatus.Ok, c.Status));
        Assert.Equal(Enum.GetValues<AudiobookCheckCode>().Length, report.Checks.Count);
    }

    [Fact]
    public async Task ARefusedShelfmarkKey_IsAProblem()
    {
        _tester.Status = ConnectionStatus.KeyRejected;

        var check = (await Run()).Checks.Single(c => c.Code == AudiobookCheckCode.ShelfmarkConnection);

        Assert.Equal(AudiobookCheckStatus.Problem, check.Status);
        Assert.Contains("SHELFMARK_API_KEY", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingCategory_IsAProblem()
    {
        _qBittorrent.Objects["torrents/categories"] = JsonNode.Parse("""{"radarr":{"name":"radarr","savePath":""}}""")!;

        var check = (await Run()).Checks.Single(c => c.Code == AudiobookCheckCode.QBittorrentCategory);

        Assert.Equal(AudiobookCheckStatus.Problem, check.Status);
    }

    [Fact]
    public async Task WithoutQBittorrent_TheCategoryIsNotChecked()
    {
        var report = await Run(qBittorrent: false);

        Assert.Equal(AudiobookCheckStatus.Skipped, report.Checks.Single(c => c.Code == AudiobookCheckCode.QBittorrentCategory).Status);
        Assert.Equal(AudiobookCheckStatus.Skipped, report.Checks.Single(c => c.Code == AudiobookCheckCode.SameFileSystem).Status);
    }

    [Fact]
    public async Task ADownloadFolderPufferfinDoesNotSee_IsAProblem()
    {
        _qBittorrent.Objects["torrents/categories"] = JsonNode.Parse("""{"audiobooks":{"name":"audiobooks","savePath":"/nowhere/audiobooks"}}""")!;

        var check = (await Run()).Checks.Single(c => c.Code == AudiobookCheckCode.SameFileSystem);

        Assert.Equal(AudiobookCheckStatus.Problem, check.Status);
        Assert.Contains("not visible from Pufferfin", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AudiobooksPufferfinCannotChange_AreAProblem_ThatNamesThem()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.UserName == "root", "root can write anything.");
        _disk.Download("Dune/Dune.m4b");
        var import = await _disk.Library.ImportAsync(Path.Combine(_disk.Downloads, "Dune"), RequestTestKit.FakeAudiobooks.Dune, TestContext.Current.CancellationToken);
        var file = Path.Combine(import.Folder, "Dune.m4b");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }
        else
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead);
        }

        var check = (await Run()).Checks.Single(c => c.Code == AudiobookCheckCode.FilesWritable);

        Assert.Equal(AudiobookCheckStatus.Problem, check.Status);
        Assert.Contains(file, check.Detail, StringComparison.Ordinal);
    }

    private sealed class FakeTester : IServiceConnectionTester
    {
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Connected;

        public Task<ConnectionTestResult> TestAsync(PufferfinService service, PufferfinSettings settings, CancellationToken cancellationToken)
            => Task.FromResult(new ConnectionTestResult(service, Status, null, TimeSpan.Zero));
    }
}
