using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// The identity of a file on disk: its device and inode (volume and file index on Windows). Two paths with the same
/// identity are hard links of one file. .NET exposes neither the inode nor hard link creation, hence the system calls.
/// </summary>
/// <param name="Device">The device (volume).</param>
/// <param name="Inode">The inode (file index).</param>
public readonly partial record struct FileIdentity(ulong Device, ulong Inode)
{
    private const int AtFdCwd = -100;
    private const uint StatxBasicStats = 0x7ff;
    private const int StatxSize = 256;
    private const int DarwinStatSize = 144;
    private const int ExdevErrno = 18;
    private const int WindowsNotSameDevice = 17;

    /// <summary>
    /// Reads the identity of a file or a directory, following symbolic links.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The identity, or <c>null</c> when the path does not exist or cannot be read.</returns>
    public static unsafe FileIdentity? Of(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (OperatingSystem.IsWindows())
        {
            return OfWindows(path);
        }

        if (OperatingSystem.IsLinux())
        {
            // The statx layout is the same on every architecture: inode at 32, device major and minor at 136 and 140.
            var buffer = stackalloc byte[StatxSize];
            if (Statx(AtFdCwd, path, 0, StatxBasicStats, buffer) != 0)
            {
                return null;
            }

            var device = ((ulong)*(uint*)(buffer + 136) << 32) | *(uint*)(buffer + 140);
            return new FileIdentity(device, *(ulong*)(buffer + 32));
        }

        if (OperatingSystem.IsMacOS())
        {
            // struct stat with 64 bit inodes: st_dev (int32) at 0, st_ino (uint64) at 8.
            var buffer = stackalloc byte[DarwinStatSize];
            var result = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? DarwinStatX64(path, buffer) : DarwinStat(path, buffer);
            return result == 0 ? new FileIdentity(*(uint*)buffer, *(ulong*)(buffer + 8)) : null;
        }

        return null;
    }

    /// <summary>
    /// Creates a hard link.
    /// </summary>
    /// <param name="existing">The existing file.</param>
    /// <param name="link">The path of the new link. Its directory must exist.</param>
    /// <exception cref="HardLinkException">The link could not be created.</exception>
    public static void CreateHardLink(string existing, string link)
    {
        ArgumentException.ThrowIfNullOrEmpty(existing);
        ArgumentException.ThrowIfNullOrEmpty(link);
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(link, existing, IntPtr.Zero))
            {
                var error = Marshal.GetLastPInvokeError();
                throw new HardLinkException(error == WindowsNotSameDevice, Marshal.GetPInvokeErrorMessage(error));
            }

            return;
        }

        if (Link(existing, link) != 0)
        {
            var errno = Marshal.GetLastPInvokeError();
            throw new HardLinkException(errno == ExdevErrno, Marshal.GetPInvokeErrorMessage(errno));
        }
    }

    private static FileIdentity? OfWindows(string path)
    {
        try
        {
            // A directory cannot be opened as a file: its volume stands for its device, which is what the callers compare.
            if (Directory.Exists(path))
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
                return new FileIdentity((ulong)StringComparer.OrdinalIgnoreCase.GetHashCode(root), 0);
            }

            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out var info)
                ? new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int Statx(int dirfd, string path, int flags, uint mask, byte* buffer);

    [LibraryImport("libc", EntryPoint = "stat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int DarwinStat(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "stat$INODE64", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial int DarwinStatX64(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Link(string existing, string link);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
