namespace Pufferfin.Server.AutoConfig;

/// <summary>
/// What triggered an auto-configuration run.
/// </summary>
public enum AutoConfigTrigger
{
    /// <summary>
    /// Server startup.
    /// </summary>
    Startup,

    /// <summary>
    /// A configuration save.
    /// </summary>
    ConfigurationSaved,

    /// <summary>
    /// An explicit request from an administrator.
    /// </summary>
    Manual
}
