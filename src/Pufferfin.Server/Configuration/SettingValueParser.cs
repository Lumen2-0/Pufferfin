using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// Parses and validates setting values into their normalized stored form.
/// Error messages never contain the submitted value: it may be a secret.
/// </summary>
public static partial class SettingValueParser
{
    private const int MaxTextLength = 4096;

    /// <summary>
    /// Parses a value given as text, as it comes from an environment variable.
    /// </summary>
    /// <param name="definition">The setting definition.</param>
    /// <param name="raw">The raw text.</param>
    /// <param name="normalized">The normalized value; <c>null</c> when the text is empty.</param>
    /// <param name="error">The validation error, when the value is invalid.</param>
    /// <returns><c>true</c> when the value is valid.</returns>
    public static bool TryParse(SettingDefinition definition, string? raw, out string? normalized, out string? error)
    {
        ArgumentNullException.ThrowIfNull(definition);

        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (raw.Length > MaxTextLength)
        {
            error = $"{definition.Key} is too long.";
            return false;
        }

        switch (definition.Kind)
        {
            case SettingKind.Url:
                return TryParseUrl(definition, raw.Trim(), out normalized, out error);
            case SettingKind.Secret:
                // Secrets are opaque: only surrounding white space is removed.
                normalized = raw.Trim();
                return true;
            case SettingKind.Text:
                normalized = raw.Trim();
                return true;
            case SettingKind.Integer:
                if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    error = $"{definition.Key} must be a whole number.";
                    return false;
                }

                return CheckRange(definition, integer, out normalized, out error);
            case SettingKind.Decimal:
                if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    error = $"{definition.Key} must be a number.";
                    return false;
                }

                return CheckRange(definition, number, out normalized, out error);
            case SettingKind.Boolean:
                return TryParseBoolean(definition, raw.Trim(), out normalized, out error);
            case SettingKind.LanguageList:
            case SettingKind.TextList:
                return TryParseList(definition, raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), out normalized, out error);
            case SettingKind.Path:
                return TryParsePath(definition, raw.Trim(), out normalized, out error);
            default:
                error = $"{definition.Key} has an unsupported type.";
                return false;
        }
    }

    /// <summary>
    /// Parses a value given as JSON, as it comes from the admin API.
    /// <c>null</c> clears the setting; strings are parsed like environment variables;
    /// numbers, booleans and arrays of strings are accepted for the matching kinds.
    /// </summary>
    /// <param name="definition">The setting definition.</param>
    /// <param name="value">The JSON value.</param>
    /// <param name="normalized">The normalized value; <c>null</c> when the setting is cleared.</param>
    /// <param name="error">The validation error, when the value is invalid.</param>
    /// <returns><c>true</c> when the value is valid.</returns>
    public static bool TryParse(SettingDefinition definition, JsonElement value, out string? normalized, out string? error)
    {
        ArgumentNullException.ThrowIfNull(definition);

        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                normalized = null;
                error = null;
                return true;
            case JsonValueKind.String:
                return TryParse(definition, value.GetString(), out normalized, out error);
            case JsonValueKind.Number when definition.Kind is SettingKind.Integer or SettingKind.Decimal:
                return TryParse(definition, value.GetRawText(), out normalized, out error);
            case JsonValueKind.True when definition.Kind == SettingKind.Boolean:
            case JsonValueKind.False when definition.Kind == SettingKind.Boolean:
                normalized = value.GetBoolean() ? "true" : "false";
                error = null;
                return true;
            case JsonValueKind.Array when definition.Kind is SettingKind.LanguageList or SettingKind.TextList:
                var items = new List<string>();
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        normalized = null;
                        error = $"{definition.Key} must be a list of strings.";
                        return false;
                    }

                    var text = item.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        items.Add(text);
                    }
                }

                return TryParseList(definition, items, out normalized, out error);
            default:
                normalized = null;
                error = $"{definition.Key} has the wrong JSON type.";
                return false;
        }
    }

    /// <summary>
    /// Splits a normalized list value.
    /// </summary>
    /// <param name="normalized">The normalized value.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<string> SplitList(string? normalized)
        => string.IsNullOrEmpty(normalized) ? [] : normalized.Split(',');

    private static bool TryParseUrl(SettingDefinition definition, string raw, out string? normalized, out string? error)
    {
        normalized = null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            error = $"{definition.Key} must be an absolute http or https URL.";
            return false;
        }

        // Credentials embedded in a URL would be returned by the API and written to logs.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = $"{definition.Key} must not contain credentials. Use the dedicated settings.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = $"{definition.Key} must not contain a query string or a fragment.";
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        error = null;
        return true;
    }

    private static bool TryParseBoolean(SettingDefinition definition, string raw, out string? normalized, out string? error)
    {
        error = null;
        switch (raw.ToLowerInvariant())
        {
            case "true":
            case "1":
            case "yes":
            case "on":
                normalized = "true";
                return true;
            case "false":
            case "0":
            case "no":
            case "off":
                normalized = "false";
                return true;
            default:
                normalized = null;
                error = $"{definition.Key} must be true or false.";
                return false;
        }
    }

    private static bool TryParsePath(SettingDefinition definition, string raw, out string? normalized, out string? error)
    {
        normalized = null;

        // The *arr and qBittorrent run in Linux containers: a path starting with '/' is absolute even on a Windows server.
        var absolute = raw.StartsWith('/') || System.IO.Path.IsPathFullyQualified(raw);
        if (!absolute || raw.Any(char.IsControl))
        {
            error = $"{definition.Key} must be an absolute directory path, such as /media.";
            return false;
        }

        if (raw.Split('/', '\\').Contains(".."))
        {
            error = $"{definition.Key} must not contain '..'.";
            return false;
        }

        var trimmed = raw.TrimEnd('/', '\\');
        normalized = trimmed.Length == 0 || trimmed.EndsWith(':') ? raw[..(trimmed.Length + 1)] : trimmed;
        error = null;
        return true;
    }

    private static bool CheckRange(SettingDefinition definition, decimal value, out string? normalized, out string? error)
    {
        if (definition.Minimum is { } minimum && value < minimum)
        {
            normalized = null;
            error = string.Create(CultureInfo.InvariantCulture, $"{definition.Key} must be at least {minimum}.");
            return false;
        }

        if (definition.Maximum is { } maximum && value > maximum)
        {
            normalized = null;
            error = string.Create(CultureInfo.InvariantCulture, $"{definition.Key} must be at most {maximum}.");
            return false;
        }

        normalized = value.ToString(CultureInfo.InvariantCulture);
        error = null;
        return true;
    }

    private static bool TryParseList(SettingDefinition definition, IEnumerable<string> items, out string? normalized, out string? error)
    {
        var regex = definition.Kind == SettingKind.LanguageList ? LanguageCodeRegex() : IdentifierRegex();
        var distinct = new List<string>();
        foreach (var item in items)
        {
            if (!regex.IsMatch(item))
            {
                normalized = null;
                error = definition.Kind == SettingKind.LanguageList
                    ? $"{definition.Key} must be a list of language codes such as \"fr\" or \"pt-BR\"."
                    : $"{definition.Key} must be a list of short identifiers (letters, digits, '-' or '_').";
                return false;
            }

            if (!distinct.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                distinct.Add(item);
            }
        }

        normalized = distinct.Count == 0 ? null : string.Join(',', distinct);
        error = null;
        return true;
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCodeRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();
}
