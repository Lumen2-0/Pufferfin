using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Storage;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Audiobooks;

/// <summary>
/// A downloads folder and a library folder on one temporary disk, and a cover server.
/// </summary>
public sealed class AudiobookDisk : IDisposable
{
    public static readonly byte[] CoverBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    public AudiobookDisk()
    {
        Root = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));
        DownloadsRoot = Path.Combine(Root, "downloads");
        Media = Path.Combine(Root, "media");
        Downloads = Path.Combine(DownloadsRoot, "audiobooks");
        LibraryRoot = Path.Combine(Media, AudiobookLibrary.FolderName);
        Directory.CreateDirectory(Downloads);
        Covers = new StubHttpMessageHandler((request, _) =>
        {
            CoverRequests++;
            return Task.FromResult(CoverAvailable
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CoverBytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        Library = new AudiobookLibrary(Covers, Links, () => LibraryRoot, NullLogger<AudiobookLibrary>.Instance);
    }

    public string Root { get; }

    // MEDIA_PATH and DOWNLOADS_PATH.
    public string Media { get; }

    public string DownloadsRoot { get; }

    public string Downloads { get; }

    public string LibraryRoot { get; }

    public StubHttpMessageHandler Covers { get; }

    public bool CoverAvailable { get; set; } = true;

    public int CoverRequests { get; private set; }

    public AudiobookLibrary Library { get; }

    // The real inodes and hard links, as in production.
    public NativeFileLinks Links { get; } = new();

    // Hard links are only supported on Linux and Windows: elsewhere the tests that need them are skipped.
    public void RequireLinks() => Assert.SkipUnless(Links.IsSupported, "This platform cannot create hard links.");

    public bool SameFile(string first, string second)
        => Links.GetIdentity(first) is { } a && Links.GetIdentity(second) is { } b && a.IsSameFile(b);

    // Writes a downloaded file under the downloads folder.
    public string Download(string relative, string content = "audio")
    {
        var path = Path.Combine(Downloads, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        Covers.Dispose();
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        Directory.Delete(Root, recursive: true);
    }
}
