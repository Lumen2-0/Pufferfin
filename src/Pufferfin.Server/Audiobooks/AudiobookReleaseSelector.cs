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
/// in another language, or whose title has other words than the book title, its author, narrator, series and tags
/// (<c>Dune Messiah</c> for <c>Dune</c>) is not picked either.
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

    // Words after which a number is a volume.
    private static readonly HashSet<string> _volumeMarkers = new(StringComparer.Ordinal) { "tome", "t", "book", "livre", "vol", "volume", "part", "partie", "cd" };

    // Words of a normalized release title that describe the release, not the book: format, edition, volume markers.
    private static readonly HashSet<string> _tagWords = new(StringComparer.Ordinal)
    {
        "audiobook", "audiobooks", "audio", "livre", "livres", "book", "tome", "t", "vol", "volume", "part", "partie",
        "cd", "unabridged", "abridged", "version", "integrale", "retail", "read", "narrated", "narrator", "by", "lu", "par",
        "mp3", "m4b", "m4a", "aac", "flac", "ogg", "opus", "kbps", "vbr", "cbr", "multi"
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
        return releases
            .Select(r => Assess(r, book, language))
            .OrderBy(r => r.Title)
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

        // A complete edition carries other words than the title by nature ("Dune, l'Integrale"): the title only has to be there.
        var completeEdition = ranked.FirstOrDefault(r => r.IsCompleteEdition && r.Title != TitleMatch.None && r.Volume != VolumeMatch.Other && r.Language != LanguageMatch.Other);
        if (completeEdition is not null)
        {
            return new AudiobookSelection(null, ranked, new RequestProblem(
                RequestProblemCode.OnlyCompleteEditions,
                "Only complete editions of the series were found. Pufferfin never takes one automatically.",
                completeEdition.Release.Title));
        }

        if (ranked.Any(r => r.Title == TitleMatch.Exact && r.Volume != VolumeMatch.Other && r.Language == LanguageMatch.Other))
        {
            return new AudiobookSelection(null, ranked, new RequestProblem(RequestProblemCode.NoAcceptableLanguage, "Releases exist, but none in the language of the request."));
        }

        // "Dune Messiah" for "Dune": maybe another book. Better no download than the wrong book.
        var partial = ranked.FirstOrDefault(r => r.Title == TitleMatch.Partial && !r.IsCompleteEdition && r.Volume != VolumeMatch.Other && r.Language != LanguageMatch.Other);
        if (partial is not null)
        {
            return new AudiobookSelection(null, ranked, new RequestProblem(
                RequestProblemCode.NoRelease,
                "Releases were found, but none is clearly this book. Pufferfin does not take one automatically.",
                partial.Release.Title));
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
    /// Gets a value indicating whether a title (of a release, or of a Shelfmark download) is this book alone, as strictly
    /// as a release is picked: exactly its title, no other volume, no other language, not a complete edition.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="book">The book.</param>
    /// <param name="language">The wanted ISO 639-1 language, or an empty string for any.</param>
    /// <returns><c>true</c> when it is the book.</returns>
    public static bool IsBook(string title, AudiobookMetadata book, string language)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(book);
        var ranked = Assess(ShelfmarkRelease.Untitled(title), book, language);
        return ranked.IsAcceptable && !ranked.IsCompleteEdition;
    }

    /// <summary>
    /// Compares a release title with the title of a book. The bracketed parts of the release title are tags (language,
    /// year, narrator, format) and are left out; once the title is found, every other word must be the author, the
    /// narrator, the series, or a known tag, else the release may be another book.
    /// </summary>
    /// <param name="releaseTitle">The release title.</param>
    /// <param name="book">The book.</param>
    /// <returns>How the title matches.</returns>
    public static TitleMatch MatchTitle(string releaseTitle, AudiobookMetadata book)
    {
        ArgumentNullException.ThrowIfNull(releaseTitle);
        ArgumentNullException.ThrowIfNull(book);
        var title = Words(MainTitle(book.Title));
        if (title.Count == 0)
        {
            return TitleMatch.None;
        }

        var words = Words(Bracketed().Replace(releaseTitle, " "));
        var at = IndexOf(words, title);
        if (at < 0)
        {
            return IndexOf(Words(releaseTitle), title) < 0 ? TitleMatch.None : TitleMatch.Partial;
        }

        words.RemoveRange(at, title.Count);
        var known = book.Authors
            .Concat(book.Narrators ?? [])
            .Append(book.SeriesName ?? string.Empty)
            .SelectMany(Words)
            .ToHashSet(StringComparer.Ordinal);
        return words.Select((word, i) => known.Contains(word) || IsTag(word, i == 0 ? null : words[i - 1])).All(tag => tag)
            ? TitleMatch.Exact
            : TitleMatch.Partial;
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

    private static RankedAudiobookRelease Assess(ShelfmarkRelease release, AudiobookMetadata book, string language)
    {
        var titleMatch = MatchTitle(release.Title, book);
        var completeEdition = IsCompleteEdition(release.Title, MainTitle(book.Title));
        var volumes = Volumes(release.Title);
        var volume = book.SeriesPosition is null || volumes.Count == 0
            ? VolumeMatch.Unknown
            : volumes.Contains(NormalizeNumber(book.SeriesPosition)) ? VolumeMatch.Same : VolumeMatch.Other;

        // Shelfmark reports a language for some sources only; the title often says it.
        var releaseLanguage = release.Language ?? TitleLanguage(release.Title);
        var languageMatch = releaseLanguage is null || string.IsNullOrEmpty(language)
            ? LanguageMatch.Unknown
            : string.Equals(releaseLanguage.Split('-', 2)[0], language.Split('-', 2)[0], StringComparison.OrdinalIgnoreCase) ? LanguageMatch.Same : LanguageMatch.Other;
        return new RankedAudiobookRelease(release, titleMatch, volume, languageMatch, completeEdition);
    }

    private static bool IsTag(string word, string? previous)
    {
        // A number alone is a volume after a volume marker, or a year; "Dune 2" may be another book.
        if (word.All(char.IsAsciiDigit))
        {
            return (previous is not null && _volumeMarkers.Contains(previous)) || (word.Length == 4 && word[0] is '1' or '2');
        }

        return _tagWords.Contains(word) || _titleLanguages.ContainsKey(word) || TagToken().IsMatch(word);
    }

    private static List<string> Words(string text) => [.. Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    private static int IndexOf(List<string> words, List<string> sequence)
    {
        for (var i = 0; i + sequence.Count <= words.Count; i++)
        {
            if (words.Skip(i).Take(sequence.Count).SequenceEqual(sequence, StringComparer.Ordinal))
            {
                return i;
            }
        }

        return -1;
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
    // "Version integrale" is an unabridged reading of one book, not a complete edition.
    [GeneratedRegex(@"\b((?<!(version|texte|lecture) )integrale|complete (series|saga|collection|set)|the complete|saga complete|serie complete|box ?set|coffret|omnibus|anthology|anthologie|collection complete|all books|tous les tomes)\b", RegexOptions.CultureInvariant)]
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

    // Raw text: "[FR]", "(2021)", "{Simon Vance}".
    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex Bracketed();

    // A normalized word that is a tag: a volume ("t02"), a bitrate ("64kbps", "128k"), a disc ("cd2").
    [GeneratedRegex(@"^(t\d+|\d+(kbps|k|khz)|(cd|part)\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TagToken();

    // Normalized text with '#' and decimal points kept.
    [GeneratedRegex(@"(?:\b(?:tome|t|book|livre|vol|volume|part|partie|episode)\s*|#\s*)0*(?<n>\d{1,3}(?:\.\d{1,2})?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeNumber();
}
