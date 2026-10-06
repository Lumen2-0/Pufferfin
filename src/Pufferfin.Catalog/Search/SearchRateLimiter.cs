using System;
using System.Threading.RateLimiting;

namespace Pufferfin.Catalog.Search;

/// <summary>
/// Per user rate limit of the catalog search, applied by Pufferfin before any external call.
/// </summary>
/// <remarks>
/// A token bucket: a burst of <see cref="BurstSize"/> searches, refilled by <see cref="SearchesPerSecond"/> per second.
/// That is enough for type-ahead suggestions with a client side debounce, and caps what one account can cost in TMDB quota.
/// </remarks>
public sealed class SearchRateLimiter : IDisposable
{
    /// <summary>
    /// The number of searches a user can send in a burst.
    /// </summary>
    public const int BurstSize = 15;

    /// <summary>
    /// The sustained number of searches per second and per user.
    /// </summary>
    public const int SearchesPerSecond = 5;

    private readonly PartitionedRateLimiter<Guid> _limiter;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchRateLimiter"/> class.
    /// </summary>
    public SearchRateLimiter()
        : this(BurstSize, SearchesPerSecond)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchRateLimiter"/> class.
    /// </summary>
    /// <param name="burstSize">The burst size.</param>
    /// <param name="perSecond">The sustained rate.</param>
    public SearchRateLimiter(int burstSize, int perSecond)
        : this(burstSize, perSecond, TimeSpan.FromSeconds(1))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchRateLimiter"/> class.
    /// </summary>
    /// <param name="burstSize">The burst size.</param>
    /// <param name="perPeriod">The searches given back at each period.</param>
    /// <param name="replenishmentPeriod">The period. The budget is refilled by a timer on the real clock, so tests
    /// use a long period: otherwise a slow machine gets searches back in the middle of a test.</param>
    internal SearchRateLimiter(int burstSize, int perPeriod, TimeSpan replenishmentPeriod)
    {
        _limiter = PartitionedRateLimiter.Create<Guid, Guid>(userId => RateLimitPartition.GetTokenBucketLimiter(
            userId,
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = burstSize,
                TokensPerPeriod = perPeriod,
                ReplenishmentPeriod = replenishmentPeriod,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    /// <summary>
    /// Takes one search from the budget of a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="retryAfter">When refused, how long to wait before retrying.</param>
    /// <returns><c>true</c> if the search may run.</returns>
    public bool TryAcquire(Guid userId, out TimeSpan retryAfter)
    {
        using var lease = _limiter.AttemptAcquire(userId);
        if (lease.IsAcquired)
        {
            retryAfter = TimeSpan.Zero;
            return true;
        }

        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay : TimeSpan.FromSeconds(1);
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _limiter.Dispose();
    }
}
