namespace Pufferfin.Server.Storage;

/// <summary>
/// Reads the size and free space of the disk holding a directory.
/// </summary>
public interface IDiskSpace
{
    /// <summary>
    /// Reads the disk holding a directory.
    /// </summary>
    /// <param name="path">The directory.</param>
    /// <returns>The total and free bytes, or <c>null</c> when the directory or its disk cannot be read.</returns>
    (long Total, long Free)? Read(string path);
}
