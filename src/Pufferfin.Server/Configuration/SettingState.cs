namespace Pufferfin.Server.Configuration;

/// <summary>
/// The effective state of one setting.
/// </summary>
/// <param name="Definition">The setting definition.</param>
/// <param name="Value">The normalized value, or <c>null</c> when empty.</param>
/// <param name="Source">Where the value comes from.</param>
/// <param name="EnvironmentIgnored">
/// <c>true</c> when the environment variable is present but ignored because an administrator owns the setting.
/// </param>
public sealed record SettingState(SettingDefinition Definition, string? Value, SettingSource Source, bool EnvironmentIgnored)
{
    /// <summary>
    /// Gets a value indicating whether the setting has a value.
    /// </summary>
    public bool IsSet => Value is not null;

    /// <summary>
    /// Gets a value indicating whether the setting is read only because the environment provides it.
    /// </summary>
    public bool IsReadOnly => Source == SettingSource.Environment;

    /// <inheritdoc />
    public override string ToString()
        => $"{nameof(SettingState)} {{ Key = {Definition.Key}, IsSet = {IsSet}, Source = {Source} }}";
}
