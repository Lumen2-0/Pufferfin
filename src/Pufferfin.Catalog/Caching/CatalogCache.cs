using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Catalog.Caching;

/// <summary>
/// Two level cache: memory first, then JSON files under <c>{cache}/pufferfin/catalog</c> so the catalog survives a restart
/// without hitting the external rate limits again.
/// </summary>
public sealed class CatalogCache : ICatalogCache, IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMemoryCache _memoryCache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogCache> _logger;
    private readonly string _directory;
    private readonly AsyncKeyedLocker<string> _locks = new(o => o.PoolSize = 20);

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogCache"/> class.
    /// </summary>
    /// <param name="memoryCache">The memory cache.</param>
    /// <param name="applicationPaths">The application paths, for the cache directory.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">The logger.</param>
    public CatalogCache(IMemoryCache memoryCache, IApplicationPaths applicationPaths, TimeProvider timeProvider, ILogger<CatalogCache> logger)
        : this(memoryCache, Path.Combine(applicationPaths.CachePath, "pufferfin", "catalog"), timeProvider, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogCache"/> class.
    /// </summary>
    /// <param name="memoryCache">The memory cache.</param>
    /// <param name="directory">The directory of the file cache.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">The logger.</param>
    public CatalogCache(IMemoryCache memoryCache, string directory, TimeProvider timeProvider, ILogger<CatalogCache> logger)
    {
        _memoryCache = memoryCache;
        _directory = directory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan timeToLive, Func<CancellationToken, Task<T?>> factory, CancellationToken cancellationToken)
        where T : class
    {
        var memoryKey = "pufferfin:catalog:" + key;
        if (_memoryCache.TryGetValue(memoryKey, out T? cached))
        {
            return cached;
        }

        using (await _locks.LockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            if (_memoryCache.TryGetValue(memoryKey, out cached))
            {
                return cached;
            }

            var path = Path.Combine(_directory, HashKey(key) + ".json");
            var fromDisk = await ReadAsync<T>(path, cancellationToken).ConfigureAwait(false);
            if (fromDisk is not null)
            {
                _memoryCache.Set(memoryKey, fromDisk.Value, fromDisk.ExpiresAt);
                return fromDisk.Value;
            }

            var value = await factory(cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                return null;
            }

            var expiresAt = _timeProvider.GetUtcNow().Add(timeToLive);
            _memoryCache.Set(memoryKey, value, expiresAt);
            await WriteAsync(path, new CacheEntry<T>(expiresAt, value), cancellationToken).ConfigureAwait(false);
            return value;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _locks.Dispose();
    }

    private static string HashKey(string key)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private async Task<CacheEntry<T>?> ReadAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                var entry = await JsonSerializer.DeserializeAsync<CacheEntry<T>>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
                if (entry?.Value is not null && entry.ExpiresAt > _timeProvider.GetUtcNow())
                {
                    return entry;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Ignoring unreadable catalog cache file {Path}", path);
        }

        return null;
    }

    private async Task WriteAsync<T>(string path, CacheEntry<T> entry, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var stream = File.Create(temporaryPath);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, entry, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The memory cache still holds the value: a read only cache directory only costs a refetch after a restart.
            _logger.LogWarning(ex, "Could not write the catalog cache file {Path}", path);
        }
    }

    private sealed record CacheEntry<T>(DateTimeOffset ExpiresAt, T Value);
}
