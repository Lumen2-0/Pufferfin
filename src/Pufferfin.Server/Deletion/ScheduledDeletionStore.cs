using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// The titles an administrator asked to delete later, in a JSON file of the configuration directory, so a restart
/// keeps them.
/// </summary>
public sealed class ScheduledDeletionStore
{
    /// <summary>
    /// The number of handled deletions kept, the latest ones.
    /// </summary>
    public const int HandledCount = 100;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger<ScheduledDeletionStore> _logger;
    private readonly Lock _lock = new();
    private List<ScheduledDeletion> _deletions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledDeletionStore"/> class.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="logger">The logger.</param>
    public ScheduledDeletionStore(string path, ILogger<ScheduledDeletionStore> logger)
    {
        _path = path;
        _logger = logger;
        _deletions = Read();
    }

    /// <summary>
    /// Gets every scheduled deletion, waiting or handled.
    /// </summary>
    /// <returns>A copy of the deletions.</returns>
    public IReadOnlyList<ScheduledDeletion> GetAll()
    {
        lock (_lock)
        {
            return _deletions.ToList();
        }
    }

    /// <summary>
    /// Gets the deletion waiting for a title, if any.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <returns>The waiting deletion, or <c>null</c>.</returns>
    public ScheduledDeletion? GetPending(Guid itemId)
    {
        lock (_lock)
        {
            return _deletions.Find(d => d.ItemId.Equals(itemId) && d.Outcome is null);
        }
    }

    /// <summary>
    /// Schedules a deletion, unless one already waits for the same title: that one is kept and returned.
    /// </summary>
    /// <param name="deletion">The deletion.</param>
    /// <returns>The deletion that waits for the title.</returns>
    public ScheduledDeletion Add(ScheduledDeletion deletion)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        lock (_lock)
        {
            if (_deletions.Find(d => d.ItemId.Equals(deletion.ItemId) && d.Outcome is null) is { } existing)
            {
                return existing;
            }

            _deletions.Add(deletion);
            Write();
            return deletion;
        }
    }

    /// <summary>
    /// Cancels the deletion waiting for a title.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <returns>Whether a deletion was waiting.</returns>
    public bool Cancel(Guid itemId)
    {
        lock (_lock)
        {
            var removed = _deletions.RemoveAll(d => d.ItemId.Equals(itemId) && d.Outcome is null);
            if (removed > 0)
            {
                Write();
            }

            return removed > 0;
        }
    }

    /// <summary>
    /// Records what became of the deletion waiting for a title. A deletion already handled is left as it is.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="now">The current time.</param>
    /// <param name="message">Why it needs an administrator, when it does.</param>
    public void Record(Guid itemId, ScheduledDeletionOutcome outcome, DateTimeOffset now, string? message = null)
    {
        lock (_lock)
        {
            var index = _deletions.FindIndex(d => d.ItemId.Equals(itemId) && d.Outcome is null);
            if (index < 0)
            {
                return;
            }

            _deletions[index] = _deletions[index] with { Outcome = outcome, HandledAt = now, Message = message };

            // The handled ones are only kept for display: the latest few.
            var handled = _deletions.Where(d => d.Outcome is not null).OrderByDescending(d => d.HandledAt).Skip(HandledCount).ToHashSet();
            _deletions.RemoveAll(handled.Contains);
            Write();
        }
    }

    private List<ScheduledDeletion> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<Document>(stream, _jsonOptions)?.Deletions ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Losing the file only loses schedules: nothing is ever deleted because of it.
            var backup = _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(_path, backup);
            _logger.LogError("The Pufferfin scheduled deletions file could not be read ({ErrorType}). It was moved to {BackupPath} and starts empty", ex.GetType().Name, backup);
            return [];
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
            JsonSerializer.Serialize(stream, new Document { Deletions = _deletions }, _jsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed class Document
    {
        public int Version { get; set; } = 1;

        public List<ScheduledDeletion> Deletions { get; set; } = [];
    }
}
