namespace Pufferfin.Server.Storage;

/// <summary>
/// What makes a file the same file under two paths: its device and its inode. Two hard links share both.
/// </summary>
/// <param name="Device">The device (on Windows, the volume serial number).</param>
/// <param name="Inode">The inode (on Windows, the file index).</param>
/// <param name="LinkCount">The number of hard links to the file.</param>
public readonly record struct FileIdentity(ulong Device, ulong Inode, uint LinkCount)
{
    /// <summary>
    /// Gets a value indicating whether two identities are the same file, whatever their link counts.
    /// </summary>
    /// <param name="other">The other identity.</param>
    /// <returns><c>true</c> for the same file.</returns>
    public bool IsSameFile(FileIdentity other) => Device == other.Device && Inode == other.Inode;
}
