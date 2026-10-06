using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Tests.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Audiobooks;

public sealed class AudiobookLibraryTests : IDisposable
{
    private static readonly AudiobookMetadata _dune = RequestTestKit.FakeAudiobooks.Dune;

    private readonly AudiobookDisk _disk = new();

    public void Dispose() => _disk.Dispose();

    [Fact]
    public async Task Import_FilesAuthorSlashTitle_AsHardLinks_WithTheCover()
    {
        var first = _disk.Download("Frank Herbert - Dune [FR]/CD1/01 - Chapitre 1.mp3");
        var second = _disk.Download("Frank Herbert - Dune [FR]/CD2/02 - Chapitre 2.mp3");
        _disk.Download("Frank Herbert - Dune [FR]/release.nfo");

        var import = await _disk.Library.ImportAsync(Path.Combine(_disk.Downloads, "Frank Herbert - Dune [FR]"), _dune, TestContext.Current.CancellationToken);

        var folder = Path.Combine(_disk.LibraryRoot, "Frank Herbert", "Dune");
        Assert.Equal(folder, import.Folder);
        Assert.Equal(2, import.Files);

        // Hard links, not copies: the same file on disk, so the torrent keeps seeding without taking more space.
        Assert.Equal(FileIdentity.Of(first), FileIdentity.Of(Path.Combine(folder, "CD1", "01 - Chapitre 1.mp3")));
        Assert.Equal(FileIdentity.Of(second), FileIdentity.Of(Path.Combine(folder, "CD2", "02 - Chapitre 2.mp3")));
        Assert.False(File.Exists(Path.Combine(folder, "release.nfo")));

        Assert.True(import.HasCover);
        Assert.Equal(AudiobookDisk.CoverBytes, await File.ReadAllBytesAsync(Path.Combine(folder, AudiobookLibrary.CoverFileName), TestContext.Current.CancellationToken));
        Assert.Empty(import.NotWritable);
        Assert.True(_disk.Library.Contains(_dune));
    }

    [Fact]
    public async Task Import_Twice_IsHarmless_AndDoesNotDownloadTheCoverAgain()
    {
        _disk.Download("Dune/Dune.m4b");
        var source = Path.Combine(_disk.Downloads, "Dune");

        await _disk.Library.ImportAsync(source, _dune, TestContext.Current.CancellationToken);
        var again = await _disk.Library.ImportAsync(source, _dune, TestContext.Current.CancellationToken);

        Assert.Equal(1, again.Files);
        Assert.Equal(1, _disk.CoverRequests);
    }

    [Fact]
    public async Task Import_OfASingleFile_FilesIt()
    {
        var file = _disk.Download("Dune.m4b");

        var import = await _disk.Library.ImportAsync(file, _dune, TestContext.Current.CancellationToken);

        Assert.Equal(FileIdentity.Of(file), FileIdentity.Of(Path.Combine(import.Folder, "Dune.m4b")));
    }

    [Fact]
    public async Task Import_WithoutCover_StillFilesTheBook()
    {
        _disk.CoverAvailable = false;
        _disk.Download("Dune/Dune.m4b");

        var import = await _disk.Library.ImportAsync(Path.Combine(_disk.Downloads, "Dune"), _dune, TestContext.Current.CancellationToken);

        Assert.False(import.HasCover);
        Assert.True(File.Exists(Path.Combine(import.Folder, "Dune.m4b")));
    }

    [Fact]
    public async Task Import_OfAPathPufferfinDoesNotSee_SaysSo()
    {
        var ex = await Assert.ThrowsAsync<AudiobookImportException>(() => _disk.Library.ImportAsync(Path.Combine(_disk.Downloads, "missing"), _dune, TestContext.Current.CancellationToken));

        Assert.Contains("not visible from Pufferfin", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_OfADownloadWithoutAudio_SaysSo()
    {
        _disk.Download("Dune/Dune.epub");

        var ex = await Assert.ThrowsAsync<AudiobookImportException>(() => _disk.Library.ImportAsync(Path.Combine(_disk.Downloads, "Dune"), _dune, TestContext.Current.CancellationToken));

        Assert.Contains("no audio file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_NeverOverwritesAnotherFile()
    {
        _disk.Download("Dune/Dune.m4b", "new");
        var existing = Path.Combine(_disk.Library.FolderOf(_dune), "Dune.m4b");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "someone else's file", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AudiobookImportException>(() => _disk.Library.ImportAsync(Path.Combine(_disk.Downloads, "Dune"), _dune, TestContext.Current.CancellationToken));

        Assert.Equal("someone else's file", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FilesPufferfinCannotChange_AreReported()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.UserName == "root", "root can write anything.");
        _disk.Download("Dune/Dune.m4b");
        var import = await _disk.Library.ImportAsync(Path.Combine(_disk.Downloads, "Dune"), _dune, TestContext.Current.CancellationToken);
        var file = Path.Combine(import.Folder, "Dune.m4b");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }
        else
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead);
        }

        Assert.Equal([file], AudiobookLibrary.FindNotWritable(_disk.LibraryRoot));
    }

    [Theory]
    [InlineData("AC/DC: Live", "AC_DC_ Live")]
    [InlineData("Who? ", "Who_")]
    [InlineData("..", "Unknown")]
    [InlineData("Le Petit Prince.", "Le Petit Prince")]
    public void Names_AreSafeOnEveryFileSystem(string name, string safe)
        => Assert.Equal(safe, AudiobookLibrary.SafeName(name));
}
