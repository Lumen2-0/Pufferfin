using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// The configuration store, persisted in a JSON file of the server configuration directory.
/// </summary>
/// <remarks>
/// On construction the file is read and reconciled with the environment
/// (see <see cref="EnvironmentReconciler"/>). Values are never logged.
/// </remarks>
public sealed class PufferfinConfigurationStore : IPufferfinConfigurationStore
{
    private readonly SettingsFile _file;
    private readonly ILogger<PufferfinConfigurationStore> _logger;
    private readonly HashSet<string> _environmentKeys;
    private readonly Lock _lock = new();
    private Dictionary<string, StoredSetting> _stored;
    private PufferfinConfigurationSnapshot _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinConfigurationStore"/> class.
    /// </summary>
    /// <param name="file">The store file.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="logger">The logger.</param>
    public PufferfinConfigurationStore(SettingsFile file, IEnvironmentReader environment, ILogger<PufferfinConfigurationStore> logger)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(environment);

        _file = file;
        _logger = logger;
        _environmentKeys = SettingKeys.All
            .Where(d => !string.IsNullOrWhiteSpace(environment.Get(d.Key)))
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);

        var fromFile = file.Read();
        var cleaned = DropInvalidStoredValues(new Dictionary<string, StoredSetting>(fromFile, StringComparer.Ordinal));
        var (reconciled, outcomes) = EnvironmentReconciler.Reconcile(cleaned, environment);
        foreach (var (key, outcome) in outcomes)
        {
            LogOutcome(key, outcome);
        }

        _stored = reconciled;
        if (fromFile.Count != reconciled.Count
            || reconciled.Any(pair => !fromFile.TryGetValue(pair.Key, out var previous) || previous != pair.Value))
        {
            _file.Write(_stored);
        }

        _current = BuildSnapshot();
    }

    /// <inheritdoc />
    public event EventHandler<PufferfinConfigurationSnapshot>? Saved;

    /// <inheritdoc />
    public PufferfinConfigurationSnapshot Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public PufferfinConfigurationSnapshot Update(IReadOnlyDictionary<string, JsonElement> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        PufferfinConfigurationSnapshot snapshot;
        lock (_lock)
        {
            var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var updated = new Dictionary<string, StoredSetting>(_stored, StringComparer.Ordinal);

            foreach (var (key, value) in changes)
            {
                if (!SettingKeys.TryGet(key, out var definition))
                {
                    AddError(errors, key, "Unknown setting.");
                    continue;
                }

                if (_current.StatesByKey[key].IsReadOnly)
                {
                    AddError(errors, key, $"{key} is set by the environment and is read only.");
                    continue;
                }

                if (definition.IsSecret && value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                {
                    // An empty field must not silently erase a secret the client never received.
                    AddError(errors, key, $"{key} cannot be blank. Send null to clear it.");
                    continue;
                }

                if (!SettingValueParser.TryParse(definition, value, out var normalized, out var error))
                {
                    AddError(errors, key, error!);
                    continue;
                }

                // An explicit clear is remembered, so the environment cannot bring the value back.
                updated[key] = new StoredSetting(normalized, FromEnvironment: false);
            }

            ValidateCrossField(updated, errors);

            if (errors.Count > 0)
            {
                throw new SettingsValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
            }

            _file.Write(updated);
            _stored = updated;
            snapshot = BuildSnapshot();
            Volatile.Write(ref _current, snapshot);
        }

        _logger.LogInformation("Pufferfin configuration saved ({Keys})", string.Join(", ", changes.Keys.Order(StringComparer.Ordinal)));
        Saved?.Invoke(this, snapshot);
        return snapshot;
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = [];
            errors[key] = list;
        }

        list.Add(message);
    }

    private static void ValidateCrossField(Dictionary<string, StoredSetting> settings, Dictionary<string, List<string>> errors)
    {
        if (settings.GetValueOrDefault(SettingKeys.SeedMinDays)?.Value is { } min
            && settings.GetValueOrDefault(SettingKeys.SeedMaxDays)?.Value is { } max
            && int.Parse(max, System.Globalization.CultureInfo.InvariantCulture) < int.Parse(min, System.Globalization.CultureInfo.InvariantCulture))
        {
            AddError(errors, SettingKeys.SeedMaxDays, $"{SettingKeys.SeedMaxDays} must be greater than or equal to {SettingKeys.SeedMinDays}.");
        }
    }

    private Dictionary<string, StoredSetting> DropInvalidStoredValues(Dictionary<string, StoredSetting> stored)
    {
        foreach (var (key, setting) in stored.ToList())
        {
            // Unknown keys are kept untouched: they may belong to a newer version.
            if (setting.Value is null || !SettingKeys.TryGet(key, out var definition))
            {
                continue;
            }

            if (!SettingValueParser.TryParse(definition, setting.Value, out var normalized, out _))
            {
                _logger.LogError("Stored Pufferfin setting {Key} is invalid and is ignored", key);
                stored.Remove(key);
            }
            else if (!string.Equals(normalized, setting.Value, StringComparison.Ordinal))
            {
                stored[key] = setting with { Value = normalized };
            }
        }

        return stored;
    }

    private void LogOutcome(string key, EnvironmentReconciler.Outcome outcome)
    {
        switch (outcome)
        {
            case EnvironmentReconciler.Outcome.Seeded:
                _logger.LogInformation("Pufferfin setting {Key} seeded from the environment", key);
                break;
            case EnvironmentReconciler.Outcome.FollowedEnvironment:
                _logger.LogInformation("Pufferfin setting {Key} updated from the environment", key);
                break;
            case EnvironmentReconciler.Outcome.EnvironmentIgnored:
                _logger.LogWarning("Environment variable {Key} is ignored: the setting was changed in the admin interface, which takes precedence", key);
                break;
            case EnvironmentReconciler.Outcome.ReleasedFromEnvironment:
                _logger.LogInformation("Environment variable {Key} was removed: the stored value is kept and is now editable", key);
                break;
            case EnvironmentReconciler.Outcome.InvalidEnvironmentValue:
                _logger.LogError("Environment variable {Key} has an invalid value and is ignored", key);
                break;
        }
    }

    private PufferfinConfigurationSnapshot BuildSnapshot()
    {
        var states = new List<SettingState>(SettingKeys.All.Count);
        foreach (var definition in SettingKeys.All)
        {
            var stored = _stored.GetValueOrDefault(definition.Key);
            var environmentPresent = _environmentKeys.Contains(definition.Key);
            var source = stored switch
            {
                null => SettingSource.Unset,
                { FromEnvironment: true } => SettingSource.Environment,
                _ => SettingSource.Store
            };

            states.Add(new SettingState(definition, stored?.Value, source, environmentPresent && source == SettingSource.Store));
        }

        return new PufferfinConfigurationSnapshot(states);
    }
}
