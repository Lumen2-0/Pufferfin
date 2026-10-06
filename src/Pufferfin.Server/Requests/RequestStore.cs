using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Requests;

/// <summary>
/// The requests, the language each user chose per series, the request permissions and the webhook token,
/// persisted in a JSON file of the server configuration directory.
/// </summary>
/// <remarks>
/// Every write goes through one lock and replaces the file atomically. A request carries a version: a writer that
/// read an older version (the processor, after a long search) cannot overwrite a newer state (a cancellation).
/// Live progress is kept in memory only.
/// </remarks>
public sealed class RequestStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger<RequestStore> _logger;
    private readonly Lock _lock = new();
    private Document _document;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestStore"/> class.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="logger">The logger.</param>
    public RequestStore(string path, ILogger<RequestStore> logger)
    {
        _path = path;
        _logger = logger;
        _document = Read();
    }

    /// <summary>
    /// Raised after a request changed, with its new version, outside the lock.
    /// </summary>
    public event EventHandler<MediaRequest>? Changed;

    /// <summary>
    /// Gets every request, in queue order.
    /// </summary>
    /// <returns>The requests.</returns>
    public IReadOnlyList<MediaRequest> GetAll()
    {
        lock (_lock)
        {
            return _document.Requests.OrderBy(r => r.Position).ToList();
        }
    }

    /// <summary>
    /// Gets a request.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The request, or <c>null</c>.</returns>
    public MediaRequest? Get(Guid id)
    {
        lock (_lock)
        {
            return _document.Requests.FirstOrDefault(r => r.Id.Equals(id));
        }
    }

    /// <summary>
    /// Adds a request at the end of the queue.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The stored request.</returns>
    public MediaRequest Add(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MediaRequest stored;
        lock (_lock)
        {
            var position = _document.Requests.Count == 0 ? 1 : _document.Requests.Max(r => r.Position) + 1;
            stored = request with { Position = position, Version = 1 };
            _document.Requests.Add(stored);
            Write();
        }

        Changed?.Invoke(this, stored);
        return stored;
    }

    /// <summary>
    /// Changes a request, unless it moved on since the caller read it.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="expectedVersion">The version the caller read, or <c>null</c> to change the current version whatever it is.</param>
    /// <param name="change">The change. Returning the same instance changes nothing.</param>
    /// <returns>The new version, or <c>null</c> when the request is gone, moved on, or did not change.</returns>
    public MediaRequest? Update(Guid id, int? expectedVersion, Func<MediaRequest, MediaRequest> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        MediaRequest updated;
        lock (_lock)
        {
            var index = _document.Requests.FindIndex(r => r.Id.Equals(id));
            if (index < 0)
            {
                return null;
            }

            var current = _document.Requests[index];
            if (expectedVersion is int version && current.Version != version)
            {
                return null;
            }

            var next = change(current);
            if (ReferenceEquals(next, current))
            {
                return null;
            }

            var persisted = !SameExceptProgress(current, next);
            updated = next with { Id = current.Id, Version = persisted ? current.Version + 1 : current.Version };
            _document.Requests[index] = updated;
            if (persisted)
            {
                Write();
            }
        }

        Changed?.Invoke(this, updated);
        return updated;
    }

    /// <summary>
    /// Moves a waiting request to a new place in the queue. Only requests waiting for approval or for a slot are ordered.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="index">The new zero based place among the waiting requests.</param>
    /// <returns><c>true</c> when the request was moved.</returns>
    public bool Move(Guid id, int index)
    {
        List<MediaRequest> moved;
        lock (_lock)
        {
            var waiting = _document.Requests
                .Where(r => r.State is RequestState.Queued or RequestState.PendingApproval)
                .OrderBy(r => r.Position)
                .ToList();
            var request = waiting.FirstOrDefault(r => r.Id.Equals(id));
            if (request is null)
            {
                return false;
            }

            var positions = waiting.Select(r => r.Position).ToList();
            waiting.Remove(request);
            waiting.Insert(Math.Clamp(index, 0, waiting.Count), request);

            // The waiting requests swap their positions: the others keep theirs.
            moved = [];
            for (var i = 0; i < waiting.Count; i++)
            {
                if (waiting[i].Position != positions[i])
                {
                    var updated = waiting[i] with { Position = positions[i], Version = waiting[i].Version + 1 };
                    _document.Requests[_document.Requests.FindIndex(r => r.Id.Equals(updated.Id))] = updated;
                    moved.Add(updated);
                }
            }

            Write();
        }

        foreach (var request in moved)
        {
            Changed?.Invoke(this, request);
        }

        return true;
    }

    /// <summary>
    /// Gets the audio language a user chose for a series.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <returns>The language, or <c>null</c> when the user never requested the series.</returns>
    public string? GetSeriesLanguage(Guid userId, int tmdbId)
    {
        lock (_lock)
        {
            return _document.SeriesLanguages.GetValueOrDefault(SeriesKey(userId, tmdbId));
        }
    }

    /// <summary>
    /// Remembers the audio language a user chose for a series.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <param name="language">The language: an ISO 639-1 code, or <see cref="MediaRequest.OriginalLanguage"/>.</param>
    public void SetSeriesLanguage(Guid userId, int tmdbId, string language)
    {
        lock (_lock)
        {
            var key = SeriesKey(userId, tmdbId);
            if (_document.SeriesLanguages.GetValueOrDefault(key) != language)
            {
                _document.SeriesLanguages[key] = language;
                Write();
            }
        }
    }

    /// <summary>
    /// Gets the request permissions of a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The permissions, <see cref="RequestPermissions.Default"/> when never set.</returns>
    public RequestPermissions GetPermissions(Guid userId)
    {
        lock (_lock)
        {
            return _document.Permissions.GetValueOrDefault(userId) ?? RequestPermissions.Default;
        }
    }

    /// <summary>
    /// Sets the request permissions of a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="permissions">The permissions.</param>
    public void SetPermissions(Guid userId, RequestPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        lock (_lock)
        {
            if (permissions == RequestPermissions.Default)
            {
                _document.Permissions.Remove(userId);
            }
            else
            {
                _document.Permissions[userId] = permissions;
            }

            Write();
        }
    }

    /// <summary>
    /// Gets the token that authenticates the notifications of Radarr and Sonarr, created on first use. Secret.
    /// </summary>
    /// <returns>The token.</returns>
    public string GetWebhookToken()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_document.WebhookToken))
            {
                _document.WebhookToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                Write();
            }

            return _document.WebhookToken;
        }
    }

    private static bool SameExceptProgress(MediaRequest left, MediaRequest right)
        => left with { Progress = null } == right with { Progress = null, Version = left.Version };

    private static string SeriesKey(Guid userId, int tmdbId)
        => userId.ToString("N", CultureInfo.InvariantCulture) + ":" + tmdbId.ToString(CultureInfo.InvariantCulture);

    private Document Read()
    {
        if (!File.Exists(_path))
        {
            return new Document();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<Document>(stream, _jsonOptions) ?? new Document();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var backup = _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(_path, backup);
            _logger.LogError("The Pufferfin requests file could not be read ({ErrorType}). It was moved to {BackupPath} and starts empty", ex.GetType().Name, backup);
            return new Document();
        }
    }

    private void Write()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _path + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, _document, _jsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed class Document
    {
        public int Version { get; set; } = 1;

        public List<MediaRequest> Requests { get; set; } = [];

        public Dictionary<string, string> SeriesLanguages { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<Guid, RequestPermissions> Permissions { get; set; } = [];

        public string? WebhookToken { get; set; }
    }
}
