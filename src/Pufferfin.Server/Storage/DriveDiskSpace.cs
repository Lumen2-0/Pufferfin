using System;
using System.IO;

namespace Pufferfin.Server.Storage;

/// <summary>
/// <see cref="IDiskSpace"/> through <see cref="DriveInfo"/>, which reads the file system of any path on Linux.
/// </summary>
public sealed class DriveDiskSpace : IDiskSpace
{
    /// <inheritdoc />
    public (long Total, long Free)? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!Directory.Exists(path))
        {
            return null;
        }

        try
        {
            var drive = new DriveInfo(path);
            return (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
