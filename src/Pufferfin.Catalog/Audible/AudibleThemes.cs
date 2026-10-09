using System.Collections.Generic;

namespace Pufferfin.Catalog.Audible;

/// <summary>
/// The search keywords of the themes, by market language.
/// </summary>
public static class AudibleThemes
{
    private static readonly Dictionary<AudibleTheme, (string En, string Fr, string De, string It)> _keywords = new()
    {
        [AudibleTheme.Horror] = ("horror", "horreur", "horror", "horror"),
        [AudibleTheme.Thriller] = ("thriller", "thriller", "thriller", "thriller"),
        [AudibleTheme.Crime] = ("detective", "policier", "krimi", "giallo"),
        [AudibleTheme.Fantasy] = ("fantasy", "fantasy", "fantasy", "fantasy"),
        [AudibleTheme.ScienceFiction] = ("science fiction", "science-fiction", "science fiction", "fantascienza"),
        [AudibleTheme.Romance] = ("romance", "romance", "liebesroman", "romanzo rosa"),
        [AudibleTheme.History] = ("history", "histoire", "geschichte", "storia"),
        [AudibleTheme.Biography] = ("biography", "biographie", "biografie", "biografia"),
        [AudibleTheme.SelfDevelopment] = ("self help", "développement personnel", "persönlichkeitsentwicklung", "crescita personale"),
        [AudibleTheme.Classics] = ("classics", "classique", "klassiker", "classici"),
        [AudibleTheme.Children] = ("children", "jeunesse", "kinder", "ragazzi"),
        [AudibleTheme.Comedy] = ("humor", "humour", "humor", "umorismo")
    };

    /// <summary>
    /// Gets the keywords of a theme for a market, in the language of the market (English by default).
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <param name="market">The market code (<c>fr</c>, <c>de</c>, <c>us</c>...).</param>
    /// <returns>The keywords.</returns>
    public static string Keywords(AudibleTheme theme, string market)
    {
        var words = _keywords[theme];
        return market.ToLowerInvariant() switch
        {
            "fr" => words.Fr,
            "de" => words.De,
            "it" => words.It,
            _ => words.En
        };
    }
}
