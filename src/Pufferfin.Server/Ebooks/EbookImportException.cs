using System;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// A book cannot be filed in the library. The message says why, in English, without a secret.
/// </summary>
public sealed class EbookImportException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EbookImportException"/> class.
    /// </summary>
    public EbookImportException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EbookImportException"/> class.
    /// </summary>
    /// <param name="message">Why the book cannot be filed.</param>
    public EbookImportException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EbookImportException"/> class.
    /// </summary>
    /// <param name="message">Why the book cannot be filed.</param>
    /// <param name="innerException">The cause.</param>
    public EbookImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
