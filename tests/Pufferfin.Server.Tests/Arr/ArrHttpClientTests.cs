using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Arr;

public sealed class ArrHttpClientTests
{
    private const string ApiKey = "secret-radarr-key-0123456789";

    private static readonly ServiceSettings _radarr = new(PufferfinService.Radarr, new Uri("http://user:hunter2@radarr:7878/base"), ApiKey);

    private static (ArrHttpClient Client, CapturingLoggerProvider Logs) Create(StubHttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var logs = new CapturingLoggerProvider();
        var client = new ArrHttpClient(handler, logs.CreateLogger<ArrHttpClient>(), timeout ?? TimeSpan.FromSeconds(5), timeout ?? TimeSpan.FromSeconds(5), TimeSpan.Zero);
        return (client, logs);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode code, string body = "[]")
        => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });

    private static void AssertNoSecret(string text)
    {
        Assert.DoesNotContain(ApiKey, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheKeyTravelsInAHeader_NeverInTheUrl()
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK));
        var (client, _) = Create(handler);

        await client.GetAsync<JsonArray>(_radarr, "api/v3/rootfolder", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(ApiKey, request.Headers.GetValues("X-Api-Key").Single());
        Assert.DoesNotContain(ApiKey, request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Equal("/base/api/v3/rootfolder", request.RequestUri.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ArrFailure.KeyRejected)]
    [InlineData(HttpStatusCode.Forbidden, ArrFailure.KeyRejected)]
    [InlineData(HttpStatusCode.NotFound, ArrFailure.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, ArrFailure.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, ArrFailure.ServerError)]
    [InlineData(HttpStatusCode.Redirect, ArrFailure.Unreachable)]
    public async Task StatusCodes_MapToAReadableFailure(HttpStatusCode code, ArrFailure expected)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(code));
        var (client, _) = Create(handler);

        var ex = await Assert.ThrowsAsync<ArrApiException>(() => client.GetAsync<JsonArray>(_radarr, "api/v3/movie", CancellationToken.None));

        Assert.Equal(expected, ex.Failure);
        Assert.Equal(PufferfinService.Radarr, ex.Service);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        AssertNoSecret(ex.Message);
    }

    [Fact]
    public async Task ATransientReadFailure_IsRetriedOnce()
    {
        var calls = 0;
        using var handler = new StubHttpMessageHandler((_, _) => Respond(Interlocked.Increment(ref calls) == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        var (client, _) = Create(handler);

        await client.GetAsync<JsonArray>(_radarr, "api/v3/queue/details?movieId=1", CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AReadFailingTwice_StopsAfterTheSecondAttempt()
    {
        using var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused (radarr:7878) " + ApiKey));
        var (client, logs) = Create(handler);

        var ex = await Assert.ThrowsAsync<ArrApiException>(() => client.GetAsync<JsonArray>(_radarr, "api/v3/movie", CancellationToken.None));

        Assert.Equal(ArrFailure.Unreachable, ex.Failure);
        Assert.Equal(ArrHttpClient.MaxReadAttempts, handler.Requests.Count);
        AssertNoSecret(ex.ToString());
        Assert.All(logs.Entries, AssertNoSecret);
    }

    [Fact]
    public async Task AWrite_IsNeverRetried_BecauseAGrabSentTwiceIsADoubleDownload()
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.ServiceUnavailable));
        var (client, _) = Create(handler);

        await Assert.ThrowsAsync<ArrApiException>(() => client.SendAsync(_radarr, HttpMethod.Post, "api/v3/release", new JsonObject(), CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ASilentService_TimesOutWithinTheExplicitLimit()
    {
        using var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (client, _) = Create(handler, TimeSpan.FromMilliseconds(200));

        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<ArrApiException>(() => client.GetAsync<JsonArray>(_radarr, "api/v3/movie", CancellationToken.None));

        Assert.Equal(ArrFailure.TimedOut, ex.Failure);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RemoteValidationMessages_AreKept_WithTheSecretsRemoved()
    {
        var body = "[{\"propertyName\":\"Path\",\"errorMessage\":\"Folder '/media/movies' does not exist\"},{\"errorMessage\":\"Key " + ApiKey + " echoed\"}]";
        using var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.BadRequest, body));
        var (client, _) = Create(handler);

        var ex = await Assert.ThrowsAsync<ArrApiException>(() => client.SendAsync(_radarr, HttpMethod.Post, "api/v3/rootfolder", new JsonObject(), CancellationToken.None));

        Assert.Equal(ArrFailure.Rejected, ex.Failure);
        Assert.Contains("Folder '/media/movies' does not exist", ex.RemoteMessages);
        Assert.All(ex.RemoteMessages, AssertNoSecret);
    }
}
