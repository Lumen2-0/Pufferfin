using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Audiobooks;

public sealed class AudiobookTorrentLocatorTests : IDisposable
{
    private readonly AudiobookTestKit _kit = new();

    private static PufferfinSettings Settings => AudiobookTestKit.Context().Settings;

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task AnImportedBook_LeadsToItsTorrent_ByInode()
    {
        _kit.Disk.RequireLinks();
        var download = _kit.CompleteTorrent();
        var import = await _kit.Disk.Library.ImportAsync(download, RequestTestKit.FakeAudiobooks.Dune, TestContext.Current.CancellationToken);

        var hash = await _kit.Locator.FindAsync(Settings, import.Folder, TestContext.Current.CancellationToken);

        Assert.Equal(AudiobookTestKit.TorrentHash, hash);
    }

    [Fact]
    public async Task ACopy_IsNotTakenForTheTorrent()
    {
        _kit.Disk.RequireLinks();
        // Same names, same content, but copied: not the files the torrent seeds.
        var download = _kit.CompleteTorrent();
        var folder = _kit.Disk.Library.FolderOf(RequestTestKit.FakeAudiobooks.Dune);
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(download))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        }

        Assert.Null(await _kit.Locator.FindAsync(Settings, folder, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFolderWithoutAudio_AsksNothingOfQBittorrent()
    {
        Assert.Null(await _kit.Locator.FindAsync(Settings, Path.Combine(_kit.Disk.LibraryRoot, "missing"), TestContext.Current.CancellationToken));
        Assert.Empty(_kit.QBittorrent.Calls);
    }
}
