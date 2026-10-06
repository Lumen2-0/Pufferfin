using System;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Http;

/// <summary>
/// A catalog source could not answer.
/// </summary>
/// <remarks>
/// The message never contains a URL, a header or a credential: it is safe to log.
/// </remarks>
public sealed class CatalogSourceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogSourceException"/> class.
    /// </summary>
    /// <param name="source">The source that failed.</param>
    /// <param name="status">The failure kind.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public CatalogSourceException(CatalogSource source, CatalogSourceStatus status, Exception? innerException = null)
        : base($"{source} request failed: {status}.", innerException)
    {
        CatalogSource = source;
        Status = status;
    }

    /// <summary>
    /// Gets the source that failed.
    /// </summary>
    public CatalogSource CatalogSource { get; }

    /// <summary>
    /// Gets the failure kind.
    /// </summary>
    public CatalogSourceStatus Status { get; }
}
