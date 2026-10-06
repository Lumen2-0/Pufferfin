using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pufferfin.Catalog.Audible;

/// <summary>
/// Reads audiobook titles: normalization and complete editions. One rule for the catalog (an Audible entry that is a
/// complete edition) and for the Requests module (a release that is one).
/// </summary>
public static partial class AudiobookTitles
{
    // Words left around a complete edition once its markers are gone: "the complete collection", "tomes".
    private static readonly HashSet<string> _completeEditionNoise = new(StringComparer.Ordinal)
    {
        "the", "complete", "collection", "saga", "serie", "series", "integrale", "tomes", "tome", "books", "book", "volumes", "edition"
    };

    /// <summary>
    /// Lowers the case, removes the accents and replaces everything but letters and digits with single spaces.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="keepHash">Whether to keep <c>#</c> and decimal points, for the volume numbers.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string text, bool keepHash = false)
    {
        ArgumentNullException.ThrowIfNull(text);
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

    /// <summary>
    /// Gets a value indicating whether a title names several books: a complete edition, a box set, or a range of
    /// volumes. The words of <paramref name="bookTitle"/> are left out, so a book called <c>The Complete Guide</c> is
    /// not taken for a complete edition.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="bookTitle">The title of the book the title is compared with, if any.</param>
    /// <returns><c>true</c> for a complete edition.</returns>
    public static bool IsCompleteEdition(string title, string? bookTitle = null)
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
    /// Gets a value indicating whether an Audible entry is a complete edition: its title, its subtitle, or its position
    /// in its series (<c>1-7</c>) names several books.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="subtitle">The subtitle.</param>
    /// <param name="seriesPositions">The positions of the entry in its series, as Audible gives them.</param>
    /// <returns><c>true</c> for a complete edition.</returns>
    public static bool IsCompleteEdition(string title, string? subtitle, IEnumerable<string?> seriesPositions)
    {
        ArgumentNullException.ThrowIfNull(seriesPositions);
        return IsCompleteEdition(title)
            || (!string.IsNullOrWhiteSpace(subtitle) && IsCompleteEdition(subtitle))
            || seriesPositions.Any(p => !string.IsNullOrWhiteSpace(p) && DashRange().IsMatch(p));
    }

    /// <summary>
    /// Gets the words that name the series of a complete edition, without the words that make it complete:
    /// <c>harry potter</c> for <c>Harry Potter - L'Integrale</c> or <c>Harry Potter, 1-7</c>.
    /// </summary>
    /// <param name="title">The title or the series of the complete edition.</param>
    /// <returns>The normalized words, empty when nothing is left.</returns>
    public static IReadOnlyList<string> CoreWords(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var normalized = " " + Normalize(DashRange().Replace(title, " ")) + " ";
        normalized = CompleteEditionWords().Replace(normalized, " ");
        normalized = VolumeRange().Replace(normalized, " ");
        normalized = BookCount().Replace(normalized, " ");
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !w.All(char.IsAsciiDigit) && !_completeEditionNoise.Contains(w))
            .ToList();
    }

    /// <summary>
    /// Reads which complete edition a text names: its number (<c>L'Integrale 2</c>), the range of volumes it holds
    /// (<c>1-7</c>, <c>Books 1-3</c>, <c>tomes 1 a 5</c>) and their count (<c>6 tomes</c>).
    /// </summary>
    /// <param name="text">The title of a complete edition, a release title, or a series name.</param>
    /// <returns>The edition; each part <c>null</c> when the text does not say.</returns>
    public static CompleteEditionNumber EditionOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        (int From, int To)? range = null;
        foreach (Match match in DashRange().Matches(text))
        {
            var from = int.Parse(match.Groups["from"].Value, CultureInfo.InvariantCulture);
            var to = int.Parse(match.Groups["to"].Value, CultureInfo.InvariantCulture);
            if (to > from)
            {
                range = (from, to);
                break;
            }
        }

        // The number is read once the ranges are gone: "L'Integrale 1-7" holds volumes 1 to 7, it is not edition 1.
        var normalized = Normalize(DashRange().Replace(text, " "));
        if (range is null && NumberedRange().Match(normalized) is { Success: true } words)
        {
            range = (int.Parse(words.Groups["from"].Value, CultureInfo.InvariantCulture), int.Parse(words.Groups["to"].Value, CultureInfo.InvariantCulture));
        }

        normalized = NumberedRange().Replace(normalized, " ");
        var number = EditionNumber().Match(normalized) is { Success: true } numbered
            ? int.Parse(numbered.Groups["n"].Value, CultureInfo.InvariantCulture)
            : (int?)null;
        var count = BookCount().Match(normalized) is { Success: true } counted
            ? int.Parse(counted.Groups[1].Value, CultureInfo.InvariantCulture)
            : (int?)null;
        return new CompleteEditionNumber(number, range, count);
    }

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

    // Normalized text: "books 1 3", "tomes 1 a 5", "t01 t06", with the bounds captured.
    [GeneratedRegex(@"\b(?:books?|tomes?|t|vol|volumes?|livres?)\s*0*(?<from>\d{1,2})\s+(?:a|to|et|and|t|tome|book|vol)?\s*0*(?<to>\d{1,2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedRange();

    // Normalized text: the number of a complete edition, "integrale 2", "l integrale tome 1", "omnibus 3", "box set 2".
    // Not a count: in "integrale 6 tomes", 6 is how many volumes it holds.
    [GeneratedRegex(@"\b(?:integrale|omnibus|coffret|box ?set|collection)\s+(?:(?:tome|t|vol|volume|n|no|numero|part|partie)\s*)?0*(?<n>\d{1,2})\b(?!\s+(?:books|tomes|volumes|livres|romans|novels)\b)", RegexOptions.CultureInvariant)]
    private static partial Regex EditionNumber();

    // "7 books", "5 tomes".
    [GeneratedRegex(@"\b([2-9]|[1-9]\d)\s+(books|tomes|volumes|livres|romans|novels)\b", RegexOptions.CultureInvariant)]
    private static partial Regex BookCount();
}
