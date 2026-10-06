namespace Pufferfin.Server.Configuration;

/// <summary>
/// Where the current value of a setting comes from.
/// </summary>
public enum SettingSource
{
    /// <summary>
    /// The setting has never been set, neither by the environment nor by an administrator.
    /// </summary>
    Unset,

    /// <summary>
    /// The value was provided by an environment variable that is still present. The setting is read only.
    /// </summary>
    Environment,

    /// <summary>
    /// The value is owned by the store: set (or cleared) by an administrator, or seeded by an
    /// environment variable that has since been removed.
    /// </summary>
    Store
}
