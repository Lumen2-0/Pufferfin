using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Http;

/// <summary>
/// Sends catalog requests with a bounded retry, an outbound rate limit and a status mapping that never leaks the request URL.
/// </summary>
internal static class CatalogHttp
{
    /// <summary>
    /// The number of attempts for a transient failure (network error, 5xx, short 429).
    /// </summary>
    internal const int MaxAttempts = 2;

    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _maxRetryAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Sends a GET request and returns the body, or <c>null</c> when the resource does not exist.
    /// </summary>
    /// <param name="client">The HTTP client, with its own timeout.</param>
    /// <param name="createRequest">Creates a fresh request for each attempt.</param>
    /// <param name="source">The source, for error reporting.</param>
    /// <param name="limiter">The outbound rate limiter of the source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response body, or <c>null</c> on 404.</returns>
    /// <exception cref="CatalogSourceException">The source failed.</exception>
    public static async Task<string?> GetStringAsync(
        HttpClient client,
        Func<HttpRequestMessage> createRequest,
        CatalogSource source,
        RateLimiter limiter,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using (var lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false))
            {
                if (!lease.IsAcquired)
                {
                    throw new CatalogSourceException(source, CatalogSourceStatus.RateLimited);
                }
            }

            var last = attempt >= MaxAttempts;
            TimeSpan delay = _retryDelay;
            try
            {
                using var request = createRequest();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                        return null;
                    case HttpStatusCode.Unauthorized:
                    case HttpStatusCode.Forbidden:
                        throw new CatalogSourceException(source, CatalogSourceStatus.Unauthorized);
                    case HttpStatusCode.TooManyRequests:
                        var retryAfter = response.Headers.RetryAfter?.Delta ?? _retryDelay;
                        if (last || retryAfter > _maxRetryAfter)
                        {
                            throw new CatalogSourceException(source, CatalogSourceStatus.RateLimited);
                        }

                        delay = retryAfter;
                        break;
                    case >= HttpStatusCode.InternalServerError:
                        if (last)
                        {
                            throw new CatalogSourceException(source, CatalogSourceStatus.Unavailable);
                        }

                        break;
                    default:
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new CatalogSourceException(source, CatalogSourceStatus.Unavailable);
                        }

                        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex)
            {
                if (last)
                {
                    // The inner exception is dropped on purpose: its message may contain the request URL.
                    throw new CatalogSourceException(source, CatalogSourceStatus.Unavailable, new HttpRequestException(ex.HttpRequestError, null, null, ex.StatusCode));
                }
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout elapsed: not retried, the caller has its own deadline.
                throw new CatalogSourceException(source, CatalogSourceStatus.TimedOut);
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
