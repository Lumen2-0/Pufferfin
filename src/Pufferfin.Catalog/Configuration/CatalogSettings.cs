using System;
using System.Collections.Generic;
using System.Linq;

namespace Pufferfin.Catalog.Configuration;

/// <summary>
/// Server wide settings consumed by the catalog connectors.
/// </summary>
/// <remarks>
/// Property names map to the configuration keys of the plan (<c>TMDB_API_KEY</c>, <c>TMDB_LANGUAGES</c>,
/// <c>AUDIBLE_MARKETS</c>). The Letterboxd account is a per user setting, see <see cref="ILetterboxdAccountProvider"/>.
/// </remarks>
public sealed class CatalogSettings
{
    /// <summary>
    /// The default TMDB language used when <c>TMDB_LANGUAGES</c> is empty.
    /// </summary>
    public const string DefaultTmdbLanguage = "en-US";

    /// <summary>
    /// Gets the TMDB credential: either a v3 API key or a v4 read access token. Secret: never logged or returned.
    /// </summary>
    public string? TmdbApiKey { get; init; }

    /// <summary>
    /// Gets the TMDB languages, in order of preference (BCP 47 tags such as <c>fr-FR</c> or <c>en</c>).
    /// </summary>
    public IReadOnlyList<string> TmdbLanguages { get; init; } = [DefaultTmdbLanguage];

    /// <summary>
    /// Gets the Audible markets to search (for example <c>fr</c>, <c>us</c>, <c>uk</c>).
    /// </summary>
    public IReadOnlyList<string> AudibleMarkets { get; init; } = [];

    /// <summary>
    /// Gets the preferred audio languages, in order of preference (ISO 639-1 codes such as <c>fr</c>).
    /// Empty when none is set: the popular audiobooks then keep every language.
    /// </summary>
    public IReadOnlyList<string> PreferredAudioLanguages { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether TMDB is configured.
    /// </summary>
    public bool IsTmdbEnabled => !string.IsNullOrWhiteSpace(TmdbApiKey);

    /// <summary>
    /// Gets a value indicating whether at least one Audible market is configured.
    /// </summary>
    public bool IsAudibleEnabled => AudibleMarkets.Count > 0;

    /// <summary>
    /// Gets the primary TMDB language.
    /// </summary>
    public string PrimaryTmdbLanguage => TmdbLanguages.Count > 0 ? TmdbLanguages[0] : DefaultTmdbLanguage;

    /// <summary>
    /// Splits a comma or semicolon separated configuration value into trimmed, distinct entries.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The entries, in their original order.</returns>
    public static IReadOnlyList<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var part in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!result.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(part);
            }
        }

        return result;
    }
}
