using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Connectivity;

public sealed class ServiceConnectionTesterTests
{
    private const string ApiKey = "0123456789abcdef-radarr-key";

    private static PufferfinSettings Settings(params (string Key, object? Value)[] changes)
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        store.Update(StoreTestContext.Changes(changes));
        return store.Current.Settings;
    }

    private static PufferfinSettings RadarrSettings()
        => Settings((SettingKeys.RadarrUrl, "http://radarr:7878/base"), (SettingKeys.RadarrApiKey, ApiKey));

    private static (ServiceConnectionTester Tester, CapturingLoggerProvider Logs) CreateTester(StubHttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var logs = new CapturingLoggerProvider();
        return (new ServiceConnectionTester(handler, logs.CreateLogger<ServiceConnectionTester>(), timeout ?? TimeSpan.FromSeconds(5)), logs);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode code, string body = "{}")
        => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });

    [Fact]
    public async Task Radarr_Answering200_IsConnected_AndTheKeyTravelsInAHeader()
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK));
        var (tester, _) = CreateTester(handler);

        var result = await tester.TestAsync(PufferfinService.Radarr, RadarrSettings(), CancellationToken.None);

        Assert.Equal(ConnectionStatus.Connected, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("http://radarr:7878/base/api/v3/system/status"), request.RequestUri);
        Assert.Equal(ApiKey, request.Headers.GetValues("X-Api-Key").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Radarr_RefusingTheKey_IsKeyRejected(HttpStatusCode code)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(code));
        var (tester, _) = CreateTester(handler);

        var result = await tester.TestAsync(PufferfinService.Radarr, RadarrSettings(), CancellationToken.None);

        Assert.Equal(ConnectionStatus.KeyRejected, result.Status);
    }

    [Fact]
    public async Task ConnectionFailure_IsUnreachable_AndNothingLeaks()
    {
        using var handler = new StubHttpMessageHandler((request, _) => throw new HttpRequestException(
            HttpRequestError.ConnectionError,
            "Connection refused " + request.RequestUri + " key " + ApiKey,
            new SocketException((int)SocketError.ConnectionRefused)));
        var (tester, logs) = CreateTester(handler);

        var result = await tester.TestAsync(PufferfinService.Radarr, RadarrSettings(), CancellationToken.None);

        Assert.Equal(ConnectionStatus.Unreachable, result.Status);
        Assert.DoesNotContain(ApiKey, result.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("radarr:7878", result.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServiceThatNeverAnswers_IsUnreachable_WithinTheTimeout()
    {
        using var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (tester, _) = CreateTester(handler, TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        var result = await tester.TestAsync(PufferfinService.Radarr, RadarrSettings(), CancellationToken.None);

        Assert.Equal(ConnectionStatus.Unreachable, result.Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "The test must not hang past its timeout.");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    public async Task UnexpectedAnswer_IsUnreachable_WithADiagnostic(HttpStatusCode code)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(code));
        var (tester, _) = CreateTester(handler);

        var result = await tester.TestAsync(PufferfinService.Radarr, RadarrSettings(), CancellationToken.None);

        Assert.Equal(ConnectionStatus.Unreachable, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Detail));
    }

    [Fact]
    public async Task UnconfiguredService_IsNotConfigured_AndNoRequestIsSent()
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK));
        var (tester, _) = CreateTester(handler);

        var result = await tester.TestAsync(PufferfinService.Sonarr, RadarrSettings(), CancellationToken.None);

        Assert.Equal(ConnectionStatus.NotConfigured, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Ok.", ConnectionStatus.Connected)]
    [InlineData("Fails.", ConnectionStatus.KeyRejected)]
    public async Task QBittorrent_LoginAnswer_IsInterpreted(string body, ConnectionStatus expected)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK, body));
        var (tester, _) = CreateTester(handler);
        var settings = Settings(
            (SettingKeys.QBittorrentUrl, "http://qbittorrent:8080"),
            (SettingKeys.QBittorrentUsername, "admin"),
            (SettingKeys.QBittorrentPassword, "qbit-password"));

        var result = await tester.TestAsync(PufferfinService.QBittorrent, settings, CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(new Uri("http://qbittorrent:8080/api/v2/auth/login"), Assert.Single(handler.Requests).RequestUri);
    }

    [Fact]
    public async Task Tmdb_ReadAccessToken_IsSentAsBearer_NotInTheUrl()
    {
        const string Token = "eyJhbGciOiJIUzI1NiJ9.payload.signature";
        using var handler = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK));
        var (tester, _) = CreateTester(handler);

        var result = await tester.TestAsync(PufferfinService.Tmdb, Settings((SettingKeys.TmdbApiKey, Token)), CancellationToken.None);

        Assert.Equal(ConnectionStatus.Connected, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.DoesNotContain(Token, request.RequestUri!.ToString(), StringComparison.Ordinal);
    }
}
