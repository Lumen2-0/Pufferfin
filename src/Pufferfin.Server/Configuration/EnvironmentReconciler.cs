using System;
using System.Collections.Generic;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// Applies the environment to the stored settings at startup.
/// </summary>
/// <remarks>
/// The store is the source of truth; the environment only seeds it. For each key:
/// <list type="bullet">
/// <item>never set and present in the environment: the environment value is copied (seeded) and the setting is read only;</item>
/// <item>seeded from the environment and the variable is still present: the setting follows the variable (it was never edited, so nothing is overwritten);</item>
/// <item>owned by the store (edited or cleared by an administrator): the environment is ignored, whatever it says;</item>
/// <item>seeded from the environment and the variable is gone: the value is kept and the store takes ownership.</item>
/// </list>
/// A redeployment therefore never overwrites a setting changed by an administrator.
/// </remarks>
public static class EnvironmentReconciler
{
    /// <summary>
    /// What the reconciliation did for one key.
    /// </summary>
    public enum Outcome
    {
        /// <summary>The setting was seeded from the environment.</summary>
        Seeded,

        /// <summary>The setting follows a changed environment value.</summary>
        FollowedEnvironment,

        /// <summary>The environment variable is present but the store owns the setting.</summary>
        EnvironmentIgnored,

        /// <summary>The environment variable disappeared; the store took ownership of the value.</summary>
        ReleasedFromEnvironment,

        /// <summary>The environment variable has an invalid value and was not applied.</summary>
        InvalidEnvironmentValue
    }

    /// <summary>
    /// Reconciles the stored settings with the environment.
    /// </summary>
    /// <param name="stored">The settings read from the store file, by key.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The reconciled settings and what happened to each affected key.</returns>
    public static (Dictionary<string, StoredSetting> Settings, List<(string Key, Outcome Outcome)> Outcomes) Reconcile(
        IReadOnlyDictionary<string, StoredSetting> stored,
        IEnvironmentReader environment)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(environment);

        var settings = new Dictionary<string, StoredSetting>(stored, StringComparer.Ordinal);
        var outcomes = new List<(string Key, Outcome Outcome)>();

        foreach (var definition in SettingKeys.All)
        {
            var key = definition.Key;
            settings.TryGetValue(key, out var current);
            var raw = environment.Get(key);

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (current is { FromEnvironment: true })
                {
                    settings[key] = current with { FromEnvironment = false };
                    outcomes.Add((key, Outcome.ReleasedFromEnvironment));
                }

                continue;
            }

            if (current is { FromEnvironment: false })
            {
                outcomes.Add((key, Outcome.EnvironmentIgnored));
                continue;
            }

            if (!SettingValueParser.TryParse(definition, raw, out var value, out _))
            {
                outcomes.Add((key, Outcome.InvalidEnvironmentValue));
                continue;
            }

            if (current is null)
            {
                settings[key] = new StoredSetting(value, FromEnvironment: true);
                outcomes.Add((key, Outcome.Seeded));
            }
            else if (!string.Equals(current.Value, value, StringComparison.Ordinal))
            {
                settings[key] = new StoredSetting(value, FromEnvironment: true);
                outcomes.Add((key, Outcome.FollowedEnvironment));
            }
        }

        return (settings, outcomes);
    }
}
