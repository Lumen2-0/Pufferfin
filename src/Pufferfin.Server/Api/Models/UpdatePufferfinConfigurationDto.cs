using System.Collections.Generic;
using System.Text.Json;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// A partial configuration update.
/// </summary>
public sealed class UpdatePufferfinConfigurationDto
{
    /// <summary>
    /// Gets the changes, by key. An absent key is left unchanged, <c>null</c> clears the setting, any
    /// other value replaces it. A secret is replaced by sending its new value; a blank string is refused.
    /// </summary>
    public required IReadOnlyDictionary<string, JsonElement> Settings { get; init; }
}
