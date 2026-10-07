using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// Ranks the ebook releases of a book and picks the one to download.
/// </summary>
/// <remarks>
/// <para>
/// The rules of the audiobooks (<see cref="AudiobookReleaseSelector"/>), one implementation for both: the right title
/// first, then the right volume, then the right language, then the quality of the source. A complete edition, an
/// omnibus or a box set is never picked automatically in place of a volume: it is offered, never imposed.
/// </para>
/// <para>
/// The quality of the source, for an ebook: the format first (EPUB, then PDF, then the comic book archives, then a ZIP
/// or RAR archive, then a release that names no format), then the seeders. A release in a format Pufferfin leaves aside
/// (<see cref="EbookFormats"/>) is never taken, and an audiobook release never comes into the ranking.
/// </para>
/// </remarks>
public static class EbookReleaseSelector
{
    // Seeders never outweigh a better format.
    private const double FormatWeight = 1_000_000;

    /// <summary>
    /// Ranks the releases of a book that Pufferfin may take: ebooks, in a format it files or opens.
    /// </summary>
    /// <param name="releases">The releases.</param>
    /// <param name="book">The book.</param>
    /// <param name="language">The wanted ISO 639-1 language.</param>
    /// <returns>The releases, best first, each with how it matches the book.</returns>
    public static IReadOnlyList<RankedAudiobookRelease> Rank(IEnumerable<ShelfmarkRelease> releases, EbookMetadata book, string language)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(book);
        return AudiobookReleaseSelector.Rank(releases.Where(IsTakeable), book.AsMatchTarget(), language, Quality);
    }

    /// <summary>
    /// Picks the release to download among the releases of a book.
    /// </summary>
    /// <param name="releases">The releases.</param>
    /// <param name="book">The book.</param>
    /// <param name="language">The wanted ISO 639-1 language.</param>
    /// <param name="excluded">The source ids never to take again, such as releases whose download failed.</param>
    /// <returns>The pick, or why there is none.</returns>
    public static AudiobookSelection Select(IEnumerable<ShelfmarkRelease> releases, EbookMetadata book, string language, IReadOnlyCollection<string>? excluded = null)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(book);
        var ebooks = releases.Where(r => IsEbook(r) && (excluded is null || !excluded.Contains(r.SourceId))).ToList();
        var takeable = ebooks.Where(IsTakeable).ToList();
        if (takeable.Count == 0 && ebooks.Count > 0)
        {
            // Releases exist, in formats the client cannot open: waiting for another one is better than a book nobody can read.
            return new AudiobookSelection(null, [], new RequestProblem(
                RequestProblemCode.NoRelease,
                "Releases were found, but only in formats Pufferfin does not file (" + string.Join(", ", ebooks.Select(EbookFormats.FormatOf).OfType<string>().Distinct(StringComparer.Ordinal)) + ").",
                ebooks[0].Title));
        }

        return AudiobookReleaseSelector.Select(takeable, book.AsMatchTarget(), language, null, Quality);
    }

    /// <summary>
    /// Gets a value indicating whether a title (of a release, or of a Shelfmark download) is this book alone, as strictly
    /// as a release is picked: exactly its title, no other volume, no other language, not a complete edition.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="book">The book.</param>
    /// <param name="language">The wanted ISO 639-1 language, or an empty string for any.</param>
    /// <returns><c>true</c> when it is the book.</returns>
    public static bool IsBook(string title, EbookMetadata book, string language)
    {
        ArgumentNullException.ThrowIfNull(book);
        return AudiobookReleaseSelector.IsBook(title, book.AsMatchTarget(), language);
    }

    // An ebook release, or one that does not say: Shelfmark searched ebooks only.
    private static bool IsEbook(ShelfmarkRelease release)
        => release.ContentType is null || string.Equals(release.ContentType, ShelfmarkClient.EbookContentType, StringComparison.OrdinalIgnoreCase);

    private static bool IsTakeable(ShelfmarkRelease release) => IsEbook(release) && EbookFormats.Rank(EbookFormats.FormatOf(release)) is not null;

    private static double Quality(ShelfmarkRelease release)
        => (-(EbookFormats.Rank(EbookFormats.FormatOf(release)) ?? int.MaxValue) * FormatWeight) + Math.Clamp(release.Seeders ?? 0, 0, FormatWeight - 1);
}
