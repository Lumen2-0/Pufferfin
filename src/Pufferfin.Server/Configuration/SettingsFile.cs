using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// Reads and writes the store file. Writes are atomic: a crash never leaves a half written file.
/// </summary>
public sealed class SettingsFile
{
    /// <summary>
    /// The current format version.
    /// </summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SettingsFile> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsFile"/> class.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="logger">The logger.</param>
    public SettingsFile(string path, ILogger<SettingsFile> logger)
    {
        Path = path;
        _logger = logger;
    }

    /// <summary>
    /// Gets the file path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Reads the stored settings. A missing file is an empty store. An unreadable file is moved
    /// aside (never deleted) and the store starts empty, so the server still starts.
    /// </summary>
    /// <returns>The stored settings, by key.</returns>
    public Dictionary<string, StoredSetting> Read()
    {
        if (!File.Exists(Path))
        {
            return new Dictionary<string, StoredSetting>(StringComparer.Ordinal);
        }

        try
        {
            using var stream = File.OpenRead(Path);
            var document = JsonSerializer.Deserialize<Document>(stream, _jsonOptions);
            if (document?.Settings is null)
            {
                throw new JsonException("The settings document is empty.");
            }

            return new Dictionary<string, StoredSetting>(document.Settings, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var backup = Path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(Path, backup);

            // The exception message can quote file content, which may hold secrets: log the type only.
            _logger.LogError("The Pufferfin settings file could not be read ({ErrorType}). It was moved to {BackupPath} and the store starts empty", ex.GetType().Name, backup);
            return new Dictionary<string, StoredSetting>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Writes the stored settings atomically.
    /// </summary>
    /// <param name="settings">The settings, by key.</param>
    public void Write(IReadOnlyDictionary<string, StoredSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sorted = new SortedDictionary<string, StoredSetting>(StringComparer.Ordinal);
        foreach (var (key, setting) in settings)
        {
            sorted[key] = setting;
        }

        var document = new Document { Version = CurrentVersion, Settings = sorted };
        var temporaryPath = Path + ".tmp";
        using (var fileStream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (!OperatingSystem.IsWindows())
            {
                // The file holds secrets: readable by the server user only.
                File.SetUnixFileMode(fileStream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            JsonSerializer.Serialize(fileStream, document, _jsonOptions);
            fileStream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, Path, overwrite: true);
    }

    private sealed class Document
    {
        public int Version { get; set; }

        public IDictionary<string, StoredSetting>? Settings { get; set; }
    }
}
