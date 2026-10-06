using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Audible;

/// <summary>
/// The Audible marketplaces and their API hosts.
/// </summary>
public static class AudibleMarkets
{
    private static readonly Dictionary<string, string> _hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = "api.audible.com",
        ["uk"] = "api.audible.co.uk",
        ["fr"] = "api.audible.fr",
        ["de"] = "api.audible.de",
        ["it"] = "api.audible.it",
        ["es"] = "api.audible.es",
        ["ca"] = "api.audible.ca",
        ["au"] = "api.audible.com.au",
        ["in"] = "api.audible.in",
        ["jp"] = "api.audible.co.jp",
        ["br"] = "api.audible.com.br"
    };

    private static readonly Dictionary<string, string> _languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "english",
        ["fr"] = "french",
        ["de"] = "german",
        ["it"] = "italian",
        ["es"] = "spanish",
        ["pt"] = "portuguese",
        ["ja"] = "japanese",
        ["nl"] = "dutch",
        ["hi"] = "hindi"
    };

    /// <summary>
    /// Gets the supported market codes.
    /// </summary>
    public static IEnumerable<string> Codes => _hosts.Keys;

    /// <summary>
    /// Gets the API base address of a market.
    /// </summary>
    /// <param name="market">The market code (<c>fr</c>, <c>us</c>, <c>uk</c>...).</param>
    /// <param name="baseAddress">The API base address.</param>
    /// <returns><c>true</c> if the market is known.</returns>
    public static bool TryGetBaseAddress(string market, out Uri? baseAddress)
    {
        if (_hosts.TryGetValue(market, out var host))
        {
            baseAddress = new Uri("https://" + host + "/1.0/");
            return true;
        }

        baseAddress = null;
        return false;
    }

    /// <summary>
    /// Converts a language code (<c>fr</c>, <c>fr-FR</c>) or an Audible language name (<c>french</c>) into the Audible name.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <returns>The Audible language name, lower case.</returns>
    public static string ToAudibleLanguage(string language)
    {
        var code = language.Split('-', 2)[0];
        return _languages.TryGetValue(code, out var name) ? name : language.ToLowerInvariant();
    }
}
