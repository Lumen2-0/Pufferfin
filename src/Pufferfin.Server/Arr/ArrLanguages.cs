using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Arr.Models;

namespace Pufferfin.Server.Arr;

/// <summary>
/// Maps the ISO 639-1 codes of the Pufferfin settings to the language names of Radarr and Sonarr.
/// </summary>
/// <remarks>
/// A fixed table rather than <see cref="System.Globalization.CultureInfo"/>: the server may run with invariant
/// globalization, and the *arr names differ from the culture names in places ("Spanish (Latino)").
/// </remarks>
public static class ArrLanguages
{
    /// <summary>
    /// The id of the "Original" language of Radarr and Sonarr.
    /// </summary>
    public const int OriginalId = -2;

    /// <summary>
    /// The id of the "Any" language of Radarr.
    /// </summary>
    public const int AnyId = -1;

    private static readonly FrozenDictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ar"] = "Arabic",
        ["bg"] = "Bulgarian",
        ["bn"] = "Bengali",
        ["ca"] = "Catalan",
        ["cs"] = "Czech",
        ["da"] = "Danish",
        ["de"] = "German",
        ["el"] = "Greek",
        ["en"] = "English",
        ["es"] = "Spanish",
        ["es-419"] = "Spanish (Latino)",
        ["et"] = "Estonian",
        ["fa"] = "Persian",
        ["fi"] = "Finnish",
        ["fr"] = "French",
        ["he"] = "Hebrew",
        ["hi"] = "Hindi",
        ["hr"] = "Croatian",
        ["hu"] = "Hungarian",
        ["id"] = "Indonesian",
        ["is"] = "Icelandic",
        ["it"] = "Italian",
        ["ja"] = "Japanese",
        ["ko"] = "Korean",
        ["lt"] = "Lithuanian",
        ["lv"] = "Latvian",
        ["ms"] = "Malay",
        ["nb"] = "Norwegian",
        ["nl"] = "Dutch",
        ["no"] = "Norwegian",
        ["pl"] = "Polish",
        ["pt"] = "Portuguese",
        ["pt-BR"] = "Portuguese (Brazil)",
        ["ro"] = "Romanian",
        ["ru"] = "Russian",
        ["sk"] = "Slovak",
        ["sl"] = "Slovenian",
        ["sr"] = "Serbian",
        ["sv"] = "Swedish",
        ["ta"] = "Tamil",
        ["te"] = "Telugu",
        ["th"] = "Thai",
        ["tr"] = "Turkish",
        ["uk"] = "Ukrainian",
        ["vi"] = "Vietnamese",
        ["zh"] = "Chinese",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the English name of a language code, as Radarr and Sonarr spell it.
    /// A regional code without its own entry falls back to its base language.
    /// </summary>
    /// <param name="code">The ISO 639-1 code, optionally with a region (<c>pt-BR</c>).</param>
    /// <returns>The name, or <c>null</c> when the language is unknown.</returns>
    public static string? GetName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (_names.TryGetValue(code, out var name))
        {
            return name;
        }

        var dash = code.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 && _names.TryGetValue(code[..dash], out var baseName) ? baseName : null;
    }

    /// <summary>
    /// Finds a language in the list of a service.
    /// </summary>
    /// <param name="code">The ISO 639-1 code.</param>
    /// <param name="languages">The languages of the service.</param>
    /// <returns>The language, or <c>null</c> when the service does not know it.</returns>
    public static ArrLanguage? Find(string code, IEnumerable<ArrLanguage> languages)
    {
        var name = GetName(code);
        return name is null ? null : languages.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a value indicating whether a release language is the given language code.
    /// </summary>
    /// <param name="language">The release language.</param>
    /// <param name="code">The ISO 639-1 code.</param>
    /// <returns><c>true</c> when they match.</returns>
    public static bool Matches(ArrLanguage language, string code)
    {
        ArgumentNullException.ThrowIfNull(language);
        var name = GetName(code);
        if (name is null || language.Name is null)
        {
            return false;
        }

        // A release in "Portuguese (Brazil)" is Portuguese; a request for "pt-BR" wants the Brazilian one.
        return string.Equals(language.Name, name, StringComparison.OrdinalIgnoreCase)
            || (!code.Contains('-', StringComparison.Ordinal) && language.Name.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase));
    }
}
