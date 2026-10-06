using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Ranks the audiobook releases of a book and picks the one to download.
/// </summary>
/// <remarks>
/// <para>
/// The ranking: the right title first, then the right volume, then the right language, then the most seeders.
/// </para>
/// <para>
/// A release is never picked automatically when it is a complete edition (several books in one release, such as
/// <c>Harry Potter 1-7</c> or <c>L'Intégrale</c>): the same rule as the season pack for a single episode. It stays in
/// the ranking, flagged, so it can be offered to the user, never imposed. A release that names another volume, that is
/// in another language, or whose title is not the book's is not picked either.
/// </para>
/// </remarks>
public static partial class AudiobookReleaseSelector
{
    // Words of a normalized release title that announce its language. Not "en", "de", "es" nor "it": they are also words.
    private static readonly Dictionary<string, string> _titleLanguages = new(StringComparer.Ordinal)
    {
        ["fr"] = "fr",
        ["french"] = "fr",
        ["francais"] = "fr",
        ["vf"] = "fr",
        ["vff"] = "fr",
        ["eng"] = "en",
        ["english"] = "en",
        ["ger"] = "de",
        ["german"] = "de",
        ["deutsch"] = "de",
        ["spa"] = "es",
        ["spanish"] = "es",
        ["espanol"] = "es",
        ["ita"] = "it",
        ["italian"] = "it",
        ["italiano"] = "it",
        ["nl"] = "nl",
        ["dutch"] = "nl",
        ["pt"] = "pt",
        ["portuguese"] = "pt",
        ["jp"] = "ja",
        ["japanese"] = "ja"
    };

    /// <summary>
    /// Ranks the releases of a book.
    /// </summary>
    /// <param name="releases">The releases.</param>
    /// <param name="book">The book.</param>
    /// <param name="language">The wanted ISO 639-1 language.</param>
    /// <returns>The releases, best first, each with how it matches the book.</returns>
    public static IReadOnlyList<RankedAudiobookRelease> Rank(IEnumerable<ShelfmarkRelease> releases, AudiobookMetadata book, string language)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(book);
        var title = MainTitle(book.Title);
        return releases
            .Select(r => Assess(r, title, book.SeriesPosition, language))
            .OrderByDescending(r => r.TitleMatches)
            .ThenBy(r => r.Volume)
            .ThenBy(r => r.Language)
            .ThenByDescending(r => r.Release.Seeders ?? 0)
            .ToList();
    }

    /// <summary>
    /// Picks the release to download among the releases of a book.
    /// </summary>
    /// <param name="releases">The releases.</param>
    /// <param name="book">The book.</param>
    /// <param name="language">The wanted ISO 639-1 language.</param>
    /// <param name="excluded">The source ids never to take again, such as releases whose download failed.</param>
    /// <returns>The pick, or why there is none.</returns>
    public static AudiobookSelection Select(IEnumerable<ShelfmarkRelease> releases, AudiobookMetadata book, string language, IReadOnlyCollection<string>? excluded = null)
    {
        var ranked = Rank(releases.Where(r => excluded is null || !excluded.Contains(r.SourceId)), book, language);
        if (ranked.Count == 0)
        {
            return new AudiobookSelection(null, ranked, new RequestProblem(RequestProblemCode.NoRelease, "No release was found yet."));
        }

        var single = ranked.FirstOrDefault(r => r.IsAcceptable && !r.IsCompleteEdition);
        if (single is not null)
        {
            return new AudiobookSelection(single.Release, ranked, null);
        }

        var completeEdition = ranked.FirstOrDefault(r => r.IsAcceptable && r.IsCompleteEdition);
        if (completeEdition is not null)
        {
            return new AudiobookSelection(null, ranked, new RequestProblem(
                RequestProblemCode.OnlyCompleteEditions,
                "Only complete editions of the series were found. Pufferfin never takes one automatically.",
                completeEdition.Release.Title));
        }

        if (ranked.Any(r => r.TitleMatches && r.Volume != VolumeMatch.Other && r.Language == LanguageMatch.Other))
        {
            return new AudiobookSelection(null, ranked, new RequestProblem(RequestProblemCode.NoAcceptableLanguage, "Releases exist, but none in the language of the request."));
        }

        return new AudiobookSelection(null, ranked, new RequestProblem(RequestProblemCode.NoRelease, "No release of this book was found yet."));
    }

    /// <summary>
    /// Gets a value indicating whether a release title names several books: a complete edition, a box set, or a range of volumes.
    /// </summary>
    /// <param name="title">The release title.</param>
    /// <returns><c>true</c> for a complete edition.</returns>
    public static bool IsCompleteEdition(string title) => IsCompleteEdition(title, null);

    /// <summary>
    /// Gets a value indicating whether a release title names several books, leaving out the words of the book title,
    /// so a book called <c>The Complete Guide</c> is not taken for a complete edition.
    /// </summary>
    /// <param name="title">The release title.</param>
    /// <param name="bookTitle">The title of the book.</param>
    /// <returns><c>true</c> for a complete edition.</returns>
    public static bool IsCompleteEdition(string title, string? bookTitle)
    {
        ArgumentNullException.ThrowIfNull(title);
        var normalized = " " + Normalize(title) + " ";
        if (!string.IsNullOrWhiteSpace(bookTitle) && Normalize(bookTitle) is { Length: > 0 } book)
        {
            normalized = normalized.Replace(" " + book + " ", " ", StringComparison.Ordinal);
        }

        if (CompleteEditionWords().IsMatch(normalized) || VolumeRange().IsMatch(normalized) || BookCount().IsMatch(normalized))
        {
            return true;
        }

        // "Harry Potter 1-7": a bare range, read before the normalization drops the dash.
        return DashRange().Matches(title).Any(m => int.Parse(m.Groups["to"].Value, CultureInfo.InvariantCulture) > int.Parse(m.Groups["from"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Gets a value indicating whether a title (of a release, or of a Shelfmark download) is this book alone: its
    /// title, no other volume, not a complete edition.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="book">The book.</param>
    /// <returns><c>true</c> when it is the book.</returns>
    public static bool IsBook(string title, AudiobookMetadata book)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(book);
        var ranked = Assess(ShelfmarkRelease.Untitled(title), MainTitle(book.Title), book.SeriesPosition, string.Empty);
        return ranked.TitleMatches && ranked.Volume != VolumeMatch.Other && !ranked.IsCompleteEdition;
    }

    /// <summary>
    /// Reads the language a release title announces (<c>[FR]</c>, <c>French</c>, <c>VF</c>, <c>English</c>...).
    /// </summary>
    /// <param name="title">The release title.</param>
    /// <returns>The ISO 639-1 code, or <c>null</c> when the title names no language, or several.</returns>
    public static string? TitleLanguage(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var found = Normalize(title).Split(' ')
            .Select(word => _titleLanguages.GetValueOrDefault(word))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>
    /// Reads the volume numbers a release title names (<c>Tome 2</c>, <c>Book 3</c>, <c>T02</c>, <c>#4</c>).
    /// </summary>
    /// <param name="title">The release title.</param>
    /// <returns>The volumes, normalized (<c>2</c>, <c>2.5</c>).</returns>
    public static IReadOnlySet<string> Volumes(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var volumes = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in VolumeNumber().Matches(Normalize(title, keepHash: true)))
        {
            volumes.Add(NormalizeNumber(match.Groups["n"].Value));
        }

        return volumes;
    }

    /// <summary>
    /// Lowers the case, removes the accents and replaces everything but letters and digits with single spaces.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="keepHash">Whether to keep <c>#</c> and decimal points, for the volume numbers.</param>
    /// <returns>The normalized text.</returns>
    internal static string Normalize(string text, bool keepHash = false)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = true;
        for (var i = 0; i < decomposed.Length; i++)
        {
            var c = decomposed[i];
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var decimalPoint = keepHash && c == '.' && i > 0 && i + 1 < decomposed.Length && char.IsDigit(decomposed[i - 1]) && char.IsDigit(decomposed[i + 1]);
            if (char.IsLetterOrDigit(c) || (keepHash && c == '#') || decimalPoint)
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static RankedAudiobookRelease Assess(ShelfmarkRelease release, string bookTitle, string? position, string language)
    {
        var title = Normalize(bookTitle);
        var normalized = Normalize(release.Title);
        var titleMatches = title.Length > 0 && (" " + normalized + " ").Contains(" " + title + " ", StringComparison.Ordinal);
        var completeEdition = IsCompleteEdition(release.Title, bookTitle);
        var volumes = Volumes(release.Title);
        var volume = position is null || volumes.Count == 0
            ? VolumeMatch.Unknown
            : volumes.Contains(NormalizeNumber(position)) ? VolumeMatch.Same : VolumeMatch.Other;

        // Shelfmark reports a language for some sources only; the title often says it.
        var releaseLanguage = release.Language ?? TitleLanguage(release.Title);
        var languageMatch = releaseLanguage is null || string.IsNullOrEmpty(language)
            ? LanguageMatch.Unknown
            : string.Equals(releaseLanguage.Split('-', 2)[0], language.Split('-', 2)[0], StringComparison.OrdinalIgnoreCase) ? LanguageMatch.Same : LanguageMatch.Other;
        return new RankedAudiobookRelease(release, titleMatches, volume, languageMatch, completeEdition);
    }

    private static string MainTitle(string title)
    {
        // "Dune: Book One of the Dune Chronicles": releases rarely carry the subtitle.
        var colon = title.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 ? title[..colon] : title;
    }

    private static string NormalizeNumber(string number)
        => decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : number;

    // Normalized text: lower case, no accent, no punctuation.
    [GeneratedRegex(@"\b(integrale|l integrale|complete (series|saga|collection|set)|the complete|saga complete|serie complete|box ?set|coffret|omnibus|anthology|anthologie|collection complete|all books|tous les tomes)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompleteEditionWords();

    // "books 1 7", "tomes 1 a 5", "t01 t05", "vol 1 to 3", "1 7" right after a series marker.
    [GeneratedRegex(@"\b(books?|tomes?|t|vol|volumes?|livres?|parts?|parties?)\s*0*\d{1,2}\s+(a|to|et|and|t|tome|book|vol)?\s*0*\d{1,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeRange();

    // Raw text: "1-7", "01 - 05", with any dash punctuation.
    [GeneratedRegex(@"(?<![\d.\p{Pd}])0*(?<from>\d{1,2})\s*\p{Pd}\s*0*(?<to>\d{1,2})(?![\d.\p{Pd}])", RegexOptions.CultureInvariant)]
    private static partial Regex DashRange();

    // "7 books", "5 tomes".
    [GeneratedRegex(@"\b([2-9]|[1-9]\d)\s+(books|tomes|volumes|livres|romans|novels)\b", RegexOptions.CultureInvariant)]
    private static partial Regex BookCount();

    // Normalized text with '#' and decimal points kept.
    [GeneratedRegex(@"(?:\b(?:tome|t|book|livre|vol|volume|part|partie|episode)\s*|#\s*)0*(?<n>\d{1,3}(?:\.\d{1,2})?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeNumber();
}
