using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pufferfin.Server.Storage;

/// <summary>
/// <see cref="IFileLinks"/> on Linux (<c>statx</c> and <c>link</c>) and Windows (file index and <c>CreateHardLinkW</c>).
/// Other platforms are not supported: the inode matching and the hard link check report it instead of guessing.
/// </summary>
public sealed partial class NativeFileLinks : IFileLinks
{
    private const int AtFdCwd = -100;
    private const uint StatxBasicStats = 0x7ff;

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    /// <inheritdoc />
    public FileIdentity? GetIdentity(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                return statx(AtFdCwd, path, 0, StatxBasicStats, out var buffer) == 0
                    ? new FileIdentity(((ulong)buffer.DevMajor << 32) | buffer.DevMinor, buffer.Ino, buffer.NLink)
                    : null;
            }

            if (OperatingSystem.IsWindows())
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return GetFileInformationByHandle(handle, out var info)
                    ? new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, info.NumberOfLinks)
                    : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }

        return null;
    }

    /// <inheritdoc />
    public bool TryCreateHardLink(string existingFile, string newLink)
    {
        ArgumentException.ThrowIfNullOrEmpty(existingFile);
        ArgumentException.ThrowIfNullOrEmpty(newLink);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                return link(existingFile, newLink) == 0;
            }

            if (OperatingSystem.IsWindows())
            {
                return CreateHardLinkW(newLink, existingFile, IntPtr.Zero);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }

        return false;
    }

    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "The C function name.")]
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int statx(int dirfd, string pathname, int flags, uint mask, out Statx statxbuf);

    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "The C function name.")]
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int link(string oldpath, string newpath);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    /// <summary>
    /// <c>struct statx</c> of Linux. Its layout is the same on every architecture, unlike <c>struct stat</c>.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Statx
    {
        [FieldOffset(16)]
        public uint NLink;

        [FieldOffset(32)]
        public ulong Ino;

        [FieldOffset(136)]
        public uint DevMajor;

        [FieldOffset(140)]
        public uint DevMinor;
    }

    /// <summary>
    /// <c>BY_HANDLE_FILE_INFORMATION</c> of Windows.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 52)]
    private struct ByHandleFileInformation
    {
        [FieldOffset(28)]
        public uint VolumeSerialNumber;

        [FieldOffset(40)]
        public uint NumberOfLinks;

        [FieldOffset(44)]
        public uint FileIndexHigh;

        [FieldOffset(48)]
        public uint FileIndexLow;
    }
}
