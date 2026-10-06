using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Users;

/// <summary>
/// Per user settings, persisted in a JSON file of the server configuration directory.
/// </summary>
public sealed partial class PufferfinUserSettingsStore : IPufferfinUserSettingsStore
{
    /// <summary>
    /// The key reported in validation errors for the Letterboxd username.
    /// </summary>
    public const string LetterboxdUsernameKey = "LetterboxdUsername";

    /// <summary>
    /// The key reported in validation errors for the preferred audio languages.
    /// </summary>
    public const string PreferredAudioLanguagesKey = "PreferredAudioLanguages";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<PufferfinUserSettingsStore> _logger;
    private readonly Lock _lock = new();
    private Dictionary<Guid, PufferfinUserSettings> _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinUserSettingsStore"/> class.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="logger">The logger.</param>
    public PufferfinUserSettingsStore(string path, ILogger<PufferfinUserSettingsStore> logger)
    {
        _path = path;
        _logger = logger;
        _users = Read();
    }

    /// <inheritdoc />
    public PufferfinUserSettings Get(Guid userId)
        => Volatile.Read(ref _users).GetValueOrDefault(userId) ?? PufferfinUserSettings.Empty;

    /// <inheritdoc />
    public PufferfinUserSettings Set(Guid userId, PufferfinUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var username = string.IsNullOrWhiteSpace(settings.LetterboxdUsername) ? null : settings.LetterboxdUsername.Trim();
        if (username is not null && !LetterboxdUsernameRegex().IsMatch(username))
        {
            throw new SettingsValidationException(new Dictionary<string, string[]>
            {
                [LetterboxdUsernameKey] = ["A Letterboxd username has 2 to 15 letters, digits or underscores."]
            });
        }

        var languageDefinition = SettingKeys.Get(SettingKeys.PreferredAudioLanguages);
        if (!SettingValueParser.TryParse(languageDefinition, string.Join(',', settings.PreferredAudioLanguages), out var languages, out _))
        {
            throw new SettingsValidationException(new Dictionary<string, string[]>
            {
                [PreferredAudioLanguagesKey] = ["Each language is an ISO 639-1 code, optionally with a region, such as fr or pt-BR."]
            });
        }

        var normalized = new PufferfinUserSettings { LetterboxdUsername = username, PreferredAudioLanguages = SettingValueParser.SplitList(languages) };
        lock (_lock)
        {
            var updated = new Dictionary<Guid, PufferfinUserSettings>(_users);
            if (normalized.LetterboxdUsername is null && normalized.PreferredAudioLanguages.Count == 0)
            {
                updated.Remove(userId);
            }
            else
            {
                updated[userId] = normalized;
            }

            Write(updated);
            Volatile.Write(ref _users, updated);
        }

        return normalized;
    }

    private Dictionary<Guid, PufferfinUserSettings> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var document = JsonSerializer.Deserialize<Document>(stream, _jsonOptions);
            return document?.Users is null ? [] : new Dictionary<Guid, PufferfinUserSettings>(document.Users);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var backup = _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(_path, backup);
            _logger.LogError("The Pufferfin user settings file could not be read ({ErrorType}). It was moved to {BackupPath} and starts empty", ex.GetType().Name, backup);
            return [];
        }
    }

    private void Write(Dictionary<Guid, PufferfinUserSettings> users)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _path + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new Document { Version = 1, Users = new SortedDictionary<Guid, PufferfinUserSettings>(users) }, _jsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    [GeneratedRegex("^[A-Za-z0-9_]{2,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex LetterboxdUsernameRegex();

    private sealed class Document
    {
        public int Version { get; set; }

        public IDictionary<Guid, PufferfinUserSettings>? Users { get; set; }
    }
}
