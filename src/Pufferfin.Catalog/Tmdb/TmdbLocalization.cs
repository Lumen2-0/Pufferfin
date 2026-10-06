using System;
using System.Collections.Generic;
using System.Linq;

namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// Picks the text of a TMDB sheet in the preferred languages, falling back to the original version rather than to nothing.
/// </summary>
internal static class TmdbLocalization
{
    /// <summary>
    /// Picks one translated field.
    /// </summary>
    /// <param name="translations">The <c>translations</c> block of the sheet.</param>
    /// <param name="preferredLanguages">The configured languages, in order (<c>fr-FR</c>, <c>en</c>).</param>
    /// <param name="originalLanguage">The ISO 639-1 original language of the title.</param>
    /// <param name="selector">Reads the field from a translation.</param>
    /// <param name="originalValue">The value in the original version (the original title), used before the translation of the original language.</param>
    /// <param name="responseValue">The value of the response itself, last resort.</param>
    /// <returns>The value and the ISO 639-1 language it is in.</returns>
    public static (string? Value, string? Language) Pick(
        TmdbModels.TranslationList? translations,
        IReadOnlyList<string> preferredLanguages,
        string? originalLanguage,
        Func<TmdbModels.TranslationData, string?> selector,
        string? originalValue,
        string? responseValue)
    {
        var all = translations?.Translations ?? [];
        foreach (var tag in preferredLanguages)
        {
            var parts = tag.Split('-', 2);
            var language = parts[0];
            var region = parts.Length > 1 ? parts[1] : null;
            var candidates = all
                .Where(t => string.Equals(t.Iso6391, language, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => region is not null && string.Equals(t.Iso31661, region, StringComparison.OrdinalIgnoreCase));
            foreach (var candidate in candidates)
            {
                var value = candidate.Data is null ? null : selector(candidate.Data);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return (value, language.ToLowerInvariant());
                }
            }

            // The original version needs no translation entry: a title in French asked in French is the original title.
            if (!string.IsNullOrWhiteSpace(originalValue) && string.Equals(language, originalLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return (originalValue, originalLanguage?.ToLowerInvariant());
            }
        }

        if (!string.IsNullOrWhiteSpace(originalValue))
        {
            return (originalValue, originalLanguage?.ToLowerInvariant());
        }

        var original = all
            .Where(t => string.Equals(t.Iso6391, originalLanguage, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Data is null ? null : selector(t.Data))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (original is not null)
        {
            return (original, originalLanguage?.ToLowerInvariant());
        }

        return string.IsNullOrWhiteSpace(responseValue) ? (null, null) : (responseValue, null);
    }
}
