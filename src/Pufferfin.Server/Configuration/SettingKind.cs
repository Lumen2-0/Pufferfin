namespace Pufferfin.Server.Configuration;

/// <summary>
/// The value type of a setting. Drives parsing, validation and how the value is exposed.
/// </summary>
public enum SettingKind
{
    /// <summary>
    /// An absolute http or https URL.
    /// </summary>
    Url,

    /// <summary>
    /// A secret (API key, password). Write only: never returned by the API.
    /// </summary>
    Secret,

    /// <summary>
    /// Free text.
    /// </summary>
    Text,

    /// <summary>
    /// A whole number.
    /// </summary>
    Integer,

    /// <summary>
    /// A decimal number.
    /// </summary>
    Decimal,

    /// <summary>
    /// A boolean.
    /// </summary>
    Boolean,

    /// <summary>
    /// An ordered list of language codes (ISO 639, optionally with a region).
    /// </summary>
    LanguageList,

    /// <summary>
    /// An ordered list of short identifiers.
    /// </summary>
    TextList,

    /// <summary>
    /// An absolute directory path, without a trailing separator.
    /// </summary>
    Path
}
