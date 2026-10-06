using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pufferfin.Catalog.Caching;

/// <summary>
/// Caches the answers of the external catalogs.
/// </summary>
public interface ICatalogCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or runs <paramref name="factory"/> once and caches its result.
    /// </summary>
    /// <remarks>
    /// Concurrent callers for the same key share one factory call. A <c>null</c> result or an exception is not cached.
    /// The key must not contain a secret: it is hashed for the file name but kept in memory as is.
    /// </remarks>
    /// <typeparam name="T">The cached type, serializable to JSON.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="timeToLive">How long the value stays valid.</param>
    /// <param name="factory">Produces the value on a miss.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value, or <c>null</c> when the factory returned <c>null</c>.</returns>
    Task<T?> GetOrCreateAsync<T>(string key, TimeSpan timeToLive, Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken)
        where T : class;
}
