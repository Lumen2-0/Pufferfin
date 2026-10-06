using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tests.TestSupport;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class CatalogHttpTests : IDisposable
{
    private readonly RateLimiter _unlimited = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 100, QueueLimit = 100 });

    private static HttpRequestMessage NewRequest() => new(HttpMethod.Get, new Uri("https://api.example.test/item"));

    [Fact]
    public async Task ServerError_IsRetriedOnce()
    {
        var calls = 0;
        using var handler = new FakeHttpHandler(_ => Interlocked.Increment(ref calls) == 1
            ? FakeHttpHandler.Status(HttpStatusCode.BadGateway)
            : FakeHttpHandler.Json("{}"));
        using var client = new HttpClient(handler);

        var body = await CatalogHttp.GetStringAsync(client, NewRequest, CatalogSource.Tmdb, _unlimited, CancellationToken.None);

        Assert.Equal("{}", body);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PersistentServerError_StopsAfterTheBoundedRetry()
    {
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<CatalogSourceException>(() => CatalogHttp.GetStringAsync(client, NewRequest, CatalogSource.Tmdb, _unlimited, CancellationToken.None));

        Assert.Equal(CatalogSourceStatus.Unavailable, ex.Status);
        Assert.Equal(CatalogHttp.MaxAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task NotFound_ReturnsNull()
    {
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        Assert.Null(await CatalogHttp.GetStringAsync(client, NewRequest, CatalogSource.Tmdb, _unlimited, CancellationToken.None));
    }

    [Fact]
    public async Task LongRetryAfter_IsReportedAsRateLimited_WithoutWaiting()
    {
        using var handler = new FakeHttpHandler(_ =>
        {
            var response = FakeHttpHandler.Status(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<CatalogSourceException>(() => CatalogHttp.GetStringAsync(client, NewRequest, CatalogSource.Tmdb, _unlimited, CancellationToken.None));

        Assert.Equal(CatalogSourceStatus.RateLimited, ex.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ClientTimeout_IsReportedAsTimedOut()
    {
        using var handler = new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpHandler.Status(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };

        var ex = await Assert.ThrowsAsync<CatalogSourceException>(() => CatalogHttp.GetStringAsync(client, NewRequest, CatalogSource.Audible, _unlimited, CancellationToken.None));

        Assert.Equal(CatalogSourceStatus.TimedOut, ex.Status);
    }

    [Fact]
    public async Task ExhaustedOutboundLimiter_IsReportedAsRateLimited()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        using var held = await limiter.AcquireAsync(1, TestContext.Current.CancellationToken);
        using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("{}"));
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<CatalogSourceException>(() => CatalogHttp.GetStringAsync(client, NewRequest, CatalogSource.Tmdb, limiter, CancellationToken.None));

        Assert.Equal(CatalogSourceStatus.RateLimited, ex.Status);
        Assert.Empty(handler.Requests);
    }

    public void Dispose() => _unlimited.Dispose();
}
