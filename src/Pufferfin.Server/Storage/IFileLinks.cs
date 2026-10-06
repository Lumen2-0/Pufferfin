namespace Pufferfin.Server.Storage;

/// <summary>
/// Inodes and hard links, which .NET does not expose.
/// </summary>
public interface IFileLinks
{
    /// <summary>
    /// Gets a value indicating whether this platform can read inodes and create hard links.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Gets the identity of a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The identity, or <c>null</c> when the file cannot be read or the platform is not supported.</returns>
    FileIdentity? GetIdentity(string path);

    /// <summary>
    /// Creates a hard link.
    /// </summary>
    /// <param name="existingFile">The existing file.</param>
    /// <param name="newLink">The path of the new link.</param>
    /// <returns><c>true</c> when the link was created.</returns>
    bool TryCreateHardLink(string existingFile, string newLink);
}
