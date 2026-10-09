using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Arr;

/// <summary>
/// Sends requests to Radarr, Sonarr, Prowlarr, Bazarr and Shelfmark: API key in a header, explicit timeout on every call,
/// one retry for reads only, and failures that never carry a URL or a key.
/// </summary>
/// <remarks>
/// Writes are never retried: a grab sent twice is a double download.
/// </remarks>
public sealed class ArrHttpClient
{
    /// <summary>
    /// The name of the HTTP client.
    /// </summary>
    public const string HttpClientName = "Pufferfin.Arr";

    /// <summary>
    /// The number of attempts of a read that failed for a transient reason.
    /// </summary>
    internal const int MaxReadAttempts = 2;

    /// <summary>
    /// The time limit of an ordinary call.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The time limit of a release search, which waits on every indexer.
    /// </summary>
    public static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ArrHttpClient> _logger;
    private readonly TimeSpan _defaultTimeout;
    private readonly TimeSpan _searchTimeout;
    private readonly TimeSpan _retryDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrHttpClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public ArrHttpClient(IHttpClientFactory httpClientFactory, ILogger<ArrHttpClient> logger)
        : this(httpClientFactory, logger, DefaultTimeout, SearchTimeout, TimeSpan.FromMilliseconds(500))
    {
    }

    internal ArrHttpClient(IHttpClientFactory httpClientFactory, ILogger<ArrHttpClient> logger, TimeSpan defaultTimeout, TimeSpan searchTimeout, TimeSpan retryDelay)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _defaultTimeout = defaultTimeout;
        _searchTimeout = searchTimeout;
        _retryDelay = retryDelay;
    }

    /// <summary>
    /// Gets the JSON options of the *arr APIs.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads a resource.
    /// </summary>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <param name="service">The service settings.</param>
    /// <param name="path">The path and query, relative to the base URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="isSearch">Whether the call is a release search, which gets the longer time limit.</param>
    /// <returns>The resource.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public async Task<T> GetAsync<T>(ServiceSettings service, string path, CancellationToken cancellationToken, bool isSearch = false)
    {
        var body = await SendCoreAsync(service, HttpMethod.Get, path, null, isSearch ? _searchTimeout : _defaultTimeout, MaxReadAttempts, cancellationToken).ConfigureAwait(false);
        return Deserialize<T>(service, body);
    }

    /// <summary>
    /// Sends a JSON body and reads the answer.
    /// </summary>
    /// <typeparam name="T">The type of the answer.</typeparam>
    /// <param name="service">The service settings.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path and query, relative to the base URL.</param>
    /// <param name="body">The body, serialized as JSON.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public async Task<T> SendAsync<T>(ServiceSettings service, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var answer = await SendAsync(service, method, path, body, cancellationToken).ConfigureAwait(false);
        return Deserialize<T>(service, answer);
    }

    /// <summary>
    /// Sends a JSON body. Never retried.
    /// </summary>
    /// <param name="service">The service settings.</param>
    /// <param name="method">The method.</param>
    /// <param name="path">The path and query, relative to the base URL.</param>
    /// <param name="body">The body, serialized as JSON.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer body.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public Task<string> SendAsync(ServiceSettings service, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        Func<HttpContent?> content = body switch
        {
            null => () => null,
            JsonNode node => () => new StringContent(node.ToJsonString(JsonOptions), Encoding.UTF8, "application/json"),
            _ => () => new StringContent(JsonSerializer.Serialize(body, body.GetType(), JsonOptions), Encoding.UTF8, "application/json")
        };
        return SendCoreAsync(service, method, path, content, _defaultTimeout, 1, cancellationToken);
    }

    /// <summary>
    /// Sends a form. Never retried.
    /// </summary>
    /// <param name="service">The service settings.</param>
    /// <param name="path">The path, relative to the base URL.</param>
    /// <param name="fields">The form fields.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer body.</returns>
    /// <exception cref="ArrApiException">The call failed.</exception>
    public Task<string> PostFormAsync(ServiceSettings service, string path, IReadOnlyList<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
        => SendCoreAsync(service, HttpMethod.Post, path, () => new FormUrlEncodedContent(fields), _defaultTimeout, 1, cancellationToken);

    internal static Uri Combine(Uri baseUrl, string path)
        => new(baseUrl.AbsoluteUri.TrimEnd('/') + "/" + path.TrimStart('/'));

    private static T Deserialize<T>(ServiceSettings service, string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions) ?? throw new ArrApiException(service.Service, ArrFailure.InvalidResponse);
        }
        catch (JsonException)
        {
            throw new ArrApiException(service.Service, ArrFailure.InvalidResponse);
        }
    }

    private static List<string> ReadRemoteMessages(ServiceSettings service, string body)
    {
        var messages = new List<string>();
        try
        {
            switch (JsonNode.Parse(body))
            {
                // Validation failures: [{ "propertyName": "...", "errorMessage": "..." }].
                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item?["errorMessage"]?.GetValue<string>() is { Length: > 0 } message)
                        {
                            messages.Add(message);
                        }
                    }

                    break;
                case JsonObject error when error["message"] is JsonValue value && value.TryGetValue<string>(out var message) && message.Length > 0:
                    messages.Add(message);
                    break;

                // Shelfmark: { "error": "..." }.
                case JsonObject error when error["error"] is JsonValue value && value.TryGetValue<string>(out var message) && message.Length > 0:
                    messages.Add(message);
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Not JSON: nothing to report.
        }

        return messages.ConvertAll(m => SecretRedactor.Redact(m, service));
    }

    private async Task<string> SendCoreAsync(
        ServiceSettings service,
        HttpMethod method,
        string path,
        Func<HttpContent?>? content,
        TimeSpan timeout,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.BaseUrl is null)
        {
            throw new ArrApiException(service.Service, ArrFailure.Unreachable);
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            var last = attempt >= maxAttempts;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                using var request = new HttpRequestMessage(method, Combine(service.BaseUrl, path)) { Content = content?.Invoke() };
                if (!string.IsNullOrEmpty(service.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("X-Api-Key", service.ApiKey);
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
                var code = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                if (code >= 500 && !last)
                {
                    _logger.LogDebug("{Service} answered HTTP {StatusCode} to {Method} {Path}, retrying", service.Service, code, method, PathOnly(path));
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogWarning("{Service} answered HTTP {StatusCode} to {Method} {Path}", service.Service, code, method, PathOnly(path));
                throw response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ArrApiException(service.Service, ArrFailure.KeyRejected, code),
                    HttpStatusCode.NotFound => new ArrApiException(service.Service, ArrFailure.NotFound, code),
                    _ when code is >= 300 and < 400 => new ArrApiException(service.Service, ArrFailure.Unreachable, code),
                    _ when code >= 500 => new ArrApiException(service.Service, ArrFailure.ServerError, code, ReadRemoteMessages(service, body)),
                    _ => new ArrApiException(service.Service, ArrFailure.Rejected, code, ReadRemoteMessages(service, body))
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The time limit elapsed: not retried, the caller has its own deadline.
                _logger.LogWarning("{Service} did not answer {Method} {Path} within {Timeout}", service.Service, method, PathOnly(path), timeout);
                throw new ArrApiException(service.Service, ArrFailure.TimedOut);
            }
            catch (HttpRequestException ex)
            {
                // The exception message may quote the URL: only its category is kept.
                _logger.LogWarning("{Service} could not be reached for {Method} {Path} ({Error})", service.Service, method, PathOnly(path), ex.HttpRequestError);
                if (last)
                {
                    throw new ArrApiException(service.Service, ArrFailure.Unreachable);
                }

                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string PathOnly(string path)
    {
        var query = path.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? path : path[..query];
    }
}
