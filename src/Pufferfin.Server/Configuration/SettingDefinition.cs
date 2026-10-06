namespace Pufferfin.Server.Configuration;

/// <summary>
/// Describes one setting of the Pufferfin configuration store.
/// </summary>
/// <param name="Key">The key. It is both the environment variable name and the field identifier.</param>
/// <param name="Kind">The value type.</param>
/// <param name="Service">The service the setting belongs to, or <c>null</c> for a general setting.</param>
/// <param name="Minimum">The inclusive minimum, for numeric settings.</param>
/// <param name="Maximum">The inclusive maximum, for numeric settings.</param>
public sealed record SettingDefinition(string Key, SettingKind Kind, PufferfinService? Service = null, decimal? Minimum = null, decimal? Maximum = null)
{
    /// <summary>
    /// Gets a value indicating whether the setting is a secret, which the API never returns.
    /// </summary>
    public bool IsSecret => Kind == SettingKind.Secret;
}
