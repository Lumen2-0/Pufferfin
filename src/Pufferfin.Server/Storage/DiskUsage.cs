namespace Pufferfin.Server.Storage;

/// <summary>
/// The usage of the disk holding a directory.
/// </summary>
/// <param name="Role">Which directory.</param>
/// <param name="Path">The directory, as this server sees it.</param>
/// <param name="Available">Whether the directory exists and its disk could be read.</param>
/// <param name="TotalBytes">The size of the disk.</param>
/// <param name="FreeBytes">The space left.</param>
/// <param name="UsedPercent">The space used, in percent.</param>
/// <param name="AboveWarning">Whether the usage reached <c>STORAGE_WARNING_PERCENT</c>.</param>
public sealed record DiskUsage(StorageRole Role, string Path, bool Available, long TotalBytes, long FreeBytes, double UsedPercent, bool AboveWarning);
