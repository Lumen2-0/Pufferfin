namespace Pufferfin.Catalog.Models;

/// <summary>
/// The outcome of a query against one catalog source.
/// </summary>
public enum CatalogSourceStatus
{
    /// <summary>The source answered.</summary>
    Ok,

    /// <summary>The source is not configured.</summary>
    Disabled,

    /// <summary>The source could not be reached or answered with an error.</summary>
    Unavailable,

    /// <summary>The source refused the configured credential.</summary>
    Unauthorized,

    /// <summary>The source, or Pufferfin on its behalf, refused the call because of a rate limit.</summary>
    RateLimited,

    /// <summary>The source did not answer in time.</summary>
    TimedOut
}
