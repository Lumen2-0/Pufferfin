using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Arr;

/// <summary>
/// The Web API v2 of qBittorrent, with its cookie session.
/// </summary>
/// <remarks>
/// The session cookie is kept in memory, by URL and user, and renewed once when qBittorrent answers 403.
/// Without a user name, qBittorrent must be set to bypass authentication for the Pufferfin address.
/// </remarks>
public sealed class QBittorrentClient
{
    /// <summary>
    /// The time limit of a call.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QBittorrentClient> _logger;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, string> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="QBittorrentClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public QBittorrentClient(IHttpClientFactory httpClientFactory, ILogger<QBittorrentClient> logger)
        : this(httpClientFactory, logger, Timeout)
    {
    }

    internal QBittorrentClient(IHttpClientFactory httpClientFactory, ILogger<QBittorrentClient> logger, TimeSpan timeout)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _timeout = timeout;
    }

    /// <summary>
    /// Gets the categories, by name.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The categories.</returns>
    public async Task<IReadOnlyDictionary<string, JsonObject>> GetCategoriesAsync(ServiceSettings service, CancellationToken cancellationToken)
    {
        var body = await SendAsync(service, HttpMethod.Get, "api/v2/torrents/categories", null, cancellationToken).ConfigureAwait(false);
        var categories = Parse<Dictionary<string, JsonObject>>(body) ?? [];
        return new Dictionary<string, JsonObject>(categories, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates a category. An empty save path lets qBittorrent use its default path and the category name.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="name">The category name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public Task CreateCategoryAsync(ServiceSettings service, string name, CancellationToken cancellationToken)
        => SendAsync(service, HttpMethod.Post, "api/v2/torrents/createCategory", [new("category", name), new("savePath", string.Empty)], cancellationToken);

    /// <summary>
    /// Gets the application preferences.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The preferences.</returns>
    public async Task<JsonObject> GetPreferencesAsync(ServiceSettings service, CancellationToken cancellationToken)
    {
        var body = await SendAsync(service, HttpMethod.Get, "api/v2/app/preferences", null, cancellationToken).ConfigureAwait(false);
        return Parse<JsonObject>(body) ?? throw new ArrApiException(PufferfinService.QBittorrent, ArrFailure.InvalidResponse);
    }

    /// <summary>
    /// Changes some preferences; the others are left as they are.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="preferences">The preferences to change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SetPreferencesAsync(ServiceSettings service, JsonObject preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return SendAsync(service, HttpMethod.Post, "api/v2/app/setPreferences", [new("json", preferences.ToJsonString())], cancellationToken);
    }

    /// <summary>
    /// Gets torrents by hash.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="hashes">The torrent hashes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The torrents found.</returns>
    public async Task<IReadOnlyList<QBittorrentTorrent>> GetTorrentsAsync(ServiceSettings service, IReadOnlyCollection<string> hashes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
        {
            return [];
        }

        var path = "api/v2/torrents/info?hashes=" + Uri.EscapeDataString(string.Join('|', hashes.Select(h => h.ToLowerInvariant())));
        var body = await SendAsync(service, HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        return Parse<List<QBittorrentTorrent>>(body) ?? [];
    }

    /// <summary>
    /// Gets every torrent.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The torrents.</returns>
    public async Task<IReadOnlyList<QBittorrentTorrent>> GetAllTorrentsAsync(ServiceSettings service, CancellationToken cancellationToken)
    {
        var body = await SendAsync(service, HttpMethod.Get, "api/v2/torrents/info", null, cancellationToken).ConfigureAwait(false);
        return Parse<List<QBittorrentTorrent>>(body) ?? [];
    }

    /// <summary>
    /// Tells whether a torrent is private, for qBittorrent versions whose torrent list does not say it.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="hash">The torrent hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the torrent is private, or <c>null</c> when qBittorrent does not say.</returns>
    public async Task<bool?> GetIsPrivateAsync(ServiceSettings service, string hash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(hash);
        var body = await SendAsync(service, HttpMethod.Get, "api/v2/torrents/properties?hash=" + Uri.EscapeDataString(hash.ToLowerInvariant()), null, cancellationToken).ConfigureAwait(false);

        // Only this field is read: the rest of the properties is dropped with the body.
        return Parse<JsonObject>(body)?["is_private"] is JsonValue value && value.TryGetValue<bool>(out var isPrivate) ? isPrivate : null;
    }

    /// <summary>
    /// Gets the files of a torrent.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="hash">The torrent hash.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The files, with their path relative to the save path.</returns>
    public async Task<IReadOnlyList<QBittorrentFile>> GetFilesAsync(ServiceSettings service, string hash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(hash);
        var body = await SendAsync(service, HttpMethod.Get, "api/v2/torrents/files?hash=" + Uri.EscapeDataString(hash.ToLowerInvariant()), null, cancellationToken).ConfigureAwait(false);
        return Parse<List<QBittorrentFile>>(body) ?? [];
    }

    /// <summary>
    /// Removes torrents. Removing a torrent qBittorrent does not have is a no-op, so a repeated call is harmless.
    /// </summary>
    /// <param name="service">The qBittorrent settings.</param>
    /// <param name="hashes">The torrent hashes.</param>
    /// <param name="deleteFiles">Whether the downloaded files are deleted too.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public Task DeleteTorrentsAsync(ServiceSettings service, IReadOnlyCollection<string> hashes, bool deleteFiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.Count == 0)
        {
            return Task.CompletedTask;
        }

        return SendAsync(
            service,
            HttpMethod.Post,
            "api/v2/torrents/delete",
            [new("hashes", string.Join('|', hashes.Select(h => h.ToLowerInvariant()))), new("deleteFiles", deleteFiles ? "true" : "false")],
            cancellationToken);
    }

    private static T? Parse<T>(string body)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, QBittorrentTorrent.JsonOptions);
        }
        catch (JsonException)
        {
            throw new ArrApiException(PufferfinService.QBittorrent, ArrFailure.InvalidResponse);
        }
    }

    private static string SessionKey(ServiceSettings service) => service.BaseUrl!.AbsoluteUri + "|" + service.Username;

    private async Task<string> SendAsync(ServiceSettings service, HttpMethod method, string path, IReadOnlyList<KeyValuePair<string, string>>? form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.BaseUrl is null)
        {
            throw new ArrApiException(PufferfinService.QBittorrent, ArrFailure.Unreachable);
        }

        var hasCredentials = !string.IsNullOrEmpty(service.Username);
        if (hasCredentials && !_sessions.ContainsKey(SessionKey(service)))
        {
            await LoginAsync(service, cancellationToken).ConfigureAwait(false);
        }

        var (code, body) = await SendOnceAsync(service, method, path, form, cancellationToken).ConfigureAwait(false);
        if (code == HttpStatusCode.Forbidden && hasCredentials)
        {
            // The session expired: log in again, once.
            _sessions.TryRemove(SessionKey(service), out _);
            await LoginAsync(service, cancellationToken).ConfigureAwait(false);
            (code, body) = await SendOnceAsync(service, method, path, form, cancellationToken).ConfigureAwait(false);
        }

        if ((int)code is >= 200 and < 300)
        {
            return body;
        }

        _logger.LogWarning("qBittorrent answered HTTP {StatusCode} to {Method} {Path}", (int)code, method, path.Split('?')[0]);
        throw code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ArrApiException(PufferfinService.QBittorrent, ArrFailure.KeyRejected, (int)code),
            HttpStatusCode.NotFound => new ArrApiException(PufferfinService.QBittorrent, ArrFailure.NotFound, (int)code),
            >= HttpStatusCode.InternalServerError => new ArrApiException(PufferfinService.QBittorrent, ArrFailure.ServerError, (int)code),
            _ => new ArrApiException(PufferfinService.QBittorrent, ArrFailure.Rejected, (int)code)
        };
    }

    private async Task LoginAsync(ServiceSettings service, CancellationToken cancellationToken)
    {
        var (code, body, cookie) = await SendRawAsync(
            service,
            HttpMethod.Post,
            "api/v2/auth/login",
            [new("username", service.Username!), new("password", service.Password ?? string.Empty)],
            null,
            cancellationToken).ConfigureAwait(false);

        // qBittorrent answers a failed login with 200 and "Fails.".
        if (code != HttpStatusCode.OK || !body.Trim().Equals("Ok.", StringComparison.OrdinalIgnoreCase) || cookie is null)
        {
            _logger.LogWarning("qBittorrent refused the Pufferfin login (HTTP {StatusCode})", (int)code);
            throw new ArrApiException(PufferfinService.QBittorrent, ArrFailure.KeyRejected, (int)code);
        }

        _sessions[SessionKey(service)] = cookie;
    }

    private async Task<(HttpStatusCode Code, string Body)> SendOnceAsync(ServiceSettings service, HttpMethod method, string path, IReadOnlyList<KeyValuePair<string, string>>? form, CancellationToken cancellationToken)
    {
        var session = _sessions.GetValueOrDefault(SessionKey(service));
        var (code, body, _) = await SendRawAsync(service, method, path, form, session, cancellationToken).ConfigureAwait(false);
        return (code, body);
    }

    private async Task<(HttpStatusCode Code, string Body, string? Cookie)> SendRawAsync(
        ServiceSettings service,
        HttpMethod method,
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? form,
        string? session,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(method, ArrHttpClient.Combine(service.BaseUrl!, path));
            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            if (session is not null)
            {
                request.Headers.TryAddWithoutValidation("Cookie", session);
            }

            // qBittorrent checks the Referer against its own host for CSRF protection.
            request.Headers.Referrer = service.BaseUrl;
            var client = _httpClientFactory.CreateClient(ArrHttpClient.HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            string? cookie = null;
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                cookie = cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith("SID=", StringComparison.Ordinal));
            }

            return (response.StatusCode, body, cookie);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("qBittorrent did not answer {Path} within {Timeout}", path.Split('?')[0], _timeout);
            throw new ArrApiException(PufferfinService.QBittorrent, ArrFailure.TimedOut);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("qBittorrent could not be reached for {Path} ({Error})", path.Split('?')[0], ex.HttpRequestError);
            throw new ArrApiException(PufferfinService.QBittorrent, ArrFailure.Unreachable);
        }
    }
}
