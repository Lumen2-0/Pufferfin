using System.IO;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// A hard link could not be created.
/// </summary>
public sealed class HardLinkException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HardLinkException"/> class.
    /// </summary>
    /// <param name="crossDevice">Whether the two paths are on different file systems.</param>
    /// <param name="reason">The reason given by the system.</param>
    public HardLinkException(bool crossDevice, string reason)
        : base(reason)
    {
        CrossDevice = crossDevice;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HardLinkException"/> class.
    /// </summary>
    public HardLinkException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HardLinkException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public HardLinkException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HardLinkException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public HardLinkException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the two paths are on different file systems, where hard links are impossible.
    /// </summary>
    public bool CrossDevice { get; }
}
