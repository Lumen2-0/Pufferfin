using System;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Arr;

/// <summary>
/// Removes the secrets of a service from a text that came from that service, before it is kept or shown.
/// </summary>
internal static class SecretRedactor
{
    private const string Mask = "[redacted]";

    /// <summary>
    /// Replaces every occurrence of the service API key and password.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="settings">The service settings holding the secrets.</param>
    /// <returns>The text without secrets.</returns>
    public static string Redact(string text, ServiceSettings settings)
    {
        var result = text;
        foreach (var secret in new[] { settings.ApiKey, settings.Password })
        {
            if (!string.IsNullOrEmpty(secret))
            {
                result = result.Replace(secret, Mask, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }
}
