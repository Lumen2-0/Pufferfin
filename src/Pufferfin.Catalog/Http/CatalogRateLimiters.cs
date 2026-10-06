using System;
using System.Threading.RateLimiting;

namespace Pufferfin.Catalog.Http;

/// <summary>
/// Outbound rate limiters, one per external catalog, shared by every request of the server.
/// </summary>
/// <remarks>
/// TMDB allows roughly 50 requests per second per IP: Pufferfin stays below with 40 and queues the excess briefly.
/// Audible and Letterboxd publish no limit: they get a conservative budget.
/// </remarks>
public sealed class CatalogRateLimiters : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogRateLimiters"/> class.
    /// </summary>
    public CatalogRateLimiters()
    {
        Tmdb = CreateBucket(40);
        Audible = CreateBucket(10);
        Letterboxd = CreateBucket(2);
    }

    /// <summary>
    /// Gets the TMDB limiter.
    /// </summary>
    public RateLimiter Tmdb { get; }

    /// <summary>
    /// Gets the Audible limiter.
    /// </summary>
    public RateLimiter Audible { get; }

    /// <summary>
    /// Gets the Letterboxd limiter.
    /// </summary>
    public RateLimiter Letterboxd { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Tmdb.Dispose();
        Audible.Dispose();
        Letterboxd.Dispose();
    }

    private static TokenBucketRateLimiter CreateBucket(int perSecond)
        => new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perSecond,
            TokensPerPeriod = perSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = perSecond * 5,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
}
