using System;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// An audiobook cannot be filed in the library. The message says why, in plain words, and carries no secret.
/// </summary>
public sealed class AudiobookImportException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookImportException"/> class.
    /// </summary>
    public AudiobookImportException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookImportException"/> class.
    /// </summary>
    /// <param name="message">The reason.</param>
    public AudiobookImportException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookImportException"/> class.
    /// </summary>
    /// <param name="message">The reason.</param>
    /// <param name="innerException">The cause.</param>
    public AudiobookImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
