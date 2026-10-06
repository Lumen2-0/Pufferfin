using System;
using System.Text.RegularExpressions;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Arr;

/// <summary>
/// Removes the secrets of a service from a text that came from that service, before it is kept or shown.
/// </summary>
internal static partial class SecretRedactor
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

    /// <summary>
    /// Replaces the secrets of every configured service, and the credentials carried by URL query parameters, such as
    /// the Prowlarr key or a tracker passkey in a download URL quoted by a service.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="settings">The settings holding the secrets.</param>
    /// <returns>The text without secrets.</returns>
    public static string Redact(string text, PufferfinSettings settings)
    {
        var result = text;
        foreach (var service in Enum.GetValues<PufferfinService>())
        {
            if (settings.GetService(service) is { } serviceSettings)
            {
                result = Redact(result, serviceSettings);
            }
        }

        return CredentialParameter().Replace(result, Mask);
    }

    [GeneratedRegex(@"(?<=[?&](?:apikey|api_key|passkey|token|key|password|pass)=)[^&\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialParameter();
}
