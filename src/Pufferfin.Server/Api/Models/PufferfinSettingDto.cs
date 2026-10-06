using System.Globalization;
using System.Linq;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// One setting, as exposed to administrators. A secret never carries its value.
/// </summary>
public sealed class PufferfinSettingDto
{
    /// <summary>
    /// Gets the key, which is also the environment variable name.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the service the setting belongs to, or <c>null</c> for a general setting.
    /// </summary>
    public PufferfinService? Service { get; init; }

    /// <summary>
    /// Gets the value type.
    /// </summary>
    public required SettingKind Type { get; init; }

    /// <summary>
    /// Gets a value indicating whether the setting is a secret. A secret is write only.
    /// </summary>
    public required bool IsSecret { get; init; }

    /// <summary>
    /// Gets the value: a string, a number, a boolean or an array of strings depending on <see cref="Type"/>.
    /// Always <c>null</c> for a secret; use <see cref="IsSet"/>.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Gets a value indicating whether the setting has a value.
    /// </summary>
    public required bool IsSet { get; init; }

    /// <summary>
    /// Gets where the value comes from.
    /// </summary>
    public required SettingSource Source { get; init; }

    /// <summary>
    /// Gets a value indicating whether the setting is read only because the environment provides it.
    /// </summary>
    public required bool IsReadOnly { get; init; }

    /// <summary>
    /// Gets a value indicating whether an environment variable is present but ignored, because the
    /// setting was changed in the admin interface.
    /// </summary>
    public required bool EnvironmentIgnored { get; init; }

    /// <summary>
    /// Creates the DTO of a setting state.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The DTO.</returns>
    public static PufferfinSettingDto From(SettingState state)
    {
        var definition = state.Definition;
        return new PufferfinSettingDto
        {
            Key = definition.Key,
            Service = definition.Service,
            Type = definition.Kind,
            IsSecret = definition.IsSecret,
            Value = definition.IsSecret ? null : ToTypedValue(definition.Kind, state.Value),
            IsSet = state.IsSet,
            Source = state.Source,
            IsReadOnly = state.IsReadOnly,
            EnvironmentIgnored = state.EnvironmentIgnored
        };
    }

    private static object? ToTypedValue(SettingKind kind, string? value)
    {
        if (value is null)
        {
            return null;
        }

        return kind switch
        {
            SettingKind.Integer => int.Parse(value, CultureInfo.InvariantCulture),
            SettingKind.Decimal => decimal.Parse(value, CultureInfo.InvariantCulture),
            SettingKind.Boolean => value == "true",
            SettingKind.LanguageList or SettingKind.TextList => SettingValueParser.SplitList(value).ToArray(),
            SettingKind.Secret => null,
            _ => value
        };
    }
}
