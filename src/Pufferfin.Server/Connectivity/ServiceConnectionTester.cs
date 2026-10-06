using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Connectivity;

/// <summary>
/// Tests service connections with one bounded HTTP request per service.
/// </summary>
/// <remarks>
/// No retry: a test reports what the service does now. Details are fixed strings written here,
/// never exception or remote messages, which can quote a URL holding a key.
/// </remarks>
public sealed class ServiceConnectionTester : IServiceConnectionTester
{
    /// <summary>
    /// The name of the HTTP client used for connection tests.
    /// </summary>
    public const string HttpClientName = "Pufferfin.ConnectionTest";

    /// <summary>
    /// The default test timeout.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly Uri _tmdbConfigurationUrl = new("https://api.themoviedb.org/3/configuration");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ServiceConnectionTester> _logger;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceConnectionTester"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public ServiceConnectionTester(IHttpClientFactory httpClientFactory, ILogger<ServiceConnectionTester> logger)
        : this(httpClientFactory, logger, DefaultTimeout)
    {
    }

    internal ServiceConnectionTester(IHttpClientFactory httpClientFactory, ILogger<ServiceConnectionTester> logger, TimeSpan timeout)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeout = timeout;
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestAsync(PufferfinService service, PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var stopwatch = Stopwatch.StartNew();
        var serviceSettings = settings.GetService(service);
        if (serviceSettings is null)
        {
            return new ConnectionTestResult(service, ConnectionStatus.NotConfigured, null, TimeSpan.Zero);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        ConnectionStatus status;
        string? detail;
        try
        {
            using var request = BuildRequest(serviceSettings);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
            (status, detail) = await InterpretAsync(service, request.Method == HttpMethod.Post, response, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status = ConnectionStatus.Unreachable;
            detail = string.Create(CultureInfo.InvariantCulture, $"No answer within {_timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            status = ConnectionStatus.Unreachable;
            detail = ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "The host name could not be resolved.",
                HttpRequestError.ConnectionError => "The connection was refused or could not be established.",
                HttpRequestError.SecureConnectionError => "The TLS handshake failed.",
                _ => "The request failed."
            };
        }

        stopwatch.Stop();
        _logger.LogInformation("Connection test for {Service}: {Status}", service, status);
        return new ConnectionTestResult(service, status, detail, stopwatch.Elapsed);
    }

    private static HttpRequestMessage BuildRequest(ServiceSettings settings)
    {
        switch (settings.Service)
        {
            case PufferfinService.Radarr:
            case PufferfinService.Sonarr:
                return WithHeader(new HttpRequestMessage(HttpMethod.Get, Combine(settings.BaseUrl!, "api/v3/system/status")), "X-Api-Key", settings.ApiKey!);
            case PufferfinService.Prowlarr:
                return WithHeader(new HttpRequestMessage(HttpMethod.Get, Combine(settings.BaseUrl!, "api/v1/system/status")), "X-Api-Key", settings.ApiKey!);
            case PufferfinService.Bazarr:
                return WithHeader(new HttpRequestMessage(HttpMethod.Get, Combine(settings.BaseUrl!, "api/system/status")), "X-API-KEY", settings.ApiKey!);
            case PufferfinService.QBittorrent:
                if (string.IsNullOrEmpty(settings.Username))
                {
                    // Without credentials, qBittorrent must be set to bypass authentication for Pufferfin.
                    return new HttpRequestMessage(HttpMethod.Get, Combine(settings.BaseUrl!, "api/v2/app/version"));
                }

                return new HttpRequestMessage(HttpMethod.Post, Combine(settings.BaseUrl!, "api/v2/auth/login"))
                {
                    Content = new FormUrlEncodedContent(
                    [
                        new KeyValuePair<string, string>("username", settings.Username),
                        new KeyValuePair<string, string>("password", settings.Password ?? string.Empty)
                    ])
                };
            case PufferfinService.Tmdb:
                // A v4 read access token is a JWT and goes in a header. A v3 key can only go in the
                // query string: this client has no request logging, and the URL is never logged here.
                if (settings.ApiKey!.Contains('.', StringComparison.Ordinal))
                {
                    var bearerRequest = new HttpRequestMessage(HttpMethod.Get, _tmdbConfigurationUrl);
                    bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                    return bearerRequest;
                }

                return new HttpRequestMessage(HttpMethod.Get, new Uri(_tmdbConfigurationUrl + "?api_key=" + Uri.EscapeDataString(settings.ApiKey)));
            case PufferfinService.Shelfmark:
                // /api/status checks the key; /api/auth/check would only reflect a browser session.
                var shelfmarkRequest = new HttpRequestMessage(HttpMethod.Get, Combine(settings.BaseUrl!, "api/status"));
                return string.IsNullOrEmpty(settings.ApiKey) ? shelfmarkRequest : WithHeader(shelfmarkRequest, "X-Api-Key", settings.ApiKey);
            case PufferfinService.FlareSolverr:
                return new HttpRequestMessage(HttpMethod.Get, Combine(settings.BaseUrl!, string.Empty));
            default:
                throw new ArgumentOutOfRangeException(nameof(settings), settings.Service, "Unknown service.");
        }
    }

    private static async Task<(ConnectionStatus Status, string? Detail)> InterpretAsync(PufferfinService service, bool isLogin, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = (int)response.StatusCode;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return (ConnectionStatus.KeyRejected, service == PufferfinService.QBittorrent
                ? "qBittorrent refused the request. Check the credentials, or whether this address is banned after failed logins."
                : "The service refused the API key.");
        }

        if (code is >= 300 and < 400)
        {
            return (ConnectionStatus.Unreachable, "The service answered with a redirect. Check the URL and any authentication in front of the service.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (ConnectionStatus.Unreachable, "The service API was not found at this URL. Check the URL and its base path.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return (ConnectionStatus.Unreachable, string.Create(CultureInfo.InvariantCulture, $"The service answered with HTTP {code}."));
        }

        if (service == PufferfinService.QBittorrent && isLogin)
        {
            // qBittorrent answers a failed login with 200 and "Fails.".
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!body.Trim().Equals("Ok.", StringComparison.OrdinalIgnoreCase))
            {
                return (ConnectionStatus.KeyRejected, "qBittorrent refused the credentials.");
            }
        }

        return (ConnectionStatus.Connected, null);
    }

    private static HttpRequestMessage WithHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }

    private static Uri Combine(Uri baseUrl, string relativePath)
        => new(baseUrl.AbsoluteUri.TrimEnd('/') + "/" + relativePath);
}
