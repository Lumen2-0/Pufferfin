namespace Pufferfin.Catalog.Audible;

/// <summary>
/// Which complete edition of a series a title names.
/// </summary>
/// <param name="Number">The number of the edition (<c>L'Integrale 2</c>), when the title gives one.</param>
/// <param name="Range">The volumes the edition holds (<c>1-7</c>), when the title gives them.</param>
/// <param name="Count">The number of volumes it holds (<c>6 tomes</c>), when the title gives it.</param>
public sealed record CompleteEditionNumber(int? Number, (int From, int To)? Range, int? Count = null)
{
    /// <summary>
    /// Gets a value indicating whether a release of a complete edition is this edition. When this edition names its
    /// range, the release must name the same range, or as many volumes; else when it names its number, the release must
    /// name the same number; an edition naming neither is the whole series, never a numbered part of it.
    /// </summary>
    /// <param name="release">The edition the release names.</param>
    /// <returns><c>true</c> when the release is this edition.</returns>
    public bool Accepts(CompleteEditionNumber release)
    {
        System.ArgumentNullException.ThrowIfNull(release);
        if (Range is { } range)
        {
            var sameVolumes = release.Range is { } releaseRange
                ? releaseRange == range
                : release.Count == range.To - range.From + 1;
            return sameVolumes && (Number is null || release.Number is null || release.Number == Number);
        }

        if (Number is { } number)
        {
            return release.Number == number;
        }

        return release.Number is null;
    }
}
