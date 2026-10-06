using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// Reads audiobooks from the Audible markets of the catalog (<c>AUDIBLE_MARKETS</c>).
/// </summary>
public sealed class AudibleAudiobookMetadata : IAudiobookMetadata
{
    private readonly AudibleCatalogService _audible;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudibleAudiobookMetadata"/> class.
    /// </summary>
    /// <param name="audible">The Audible catalog.</param>
    public AudibleAudiobookMetadata(AudibleCatalogService audible)
    {
        _audible = audible;
    }

    /// <inheritdoc />
    public async Task<AudiobookMetadata?> GetAsync(string asin, string? market, CancellationToken cancellationToken)
    {
        var item = await _audible.GetProductAsync(asin, market, cancellationToken).ConfigureAwait(false);
        return item is null ? null : From(item);
    }

    /// <summary>
    /// Converts a catalog entry.
    /// </summary>
    /// <param name="item">The catalog entry of an audiobook.</param>
    /// <returns>The metadata.</returns>
    public static AudiobookMetadata From(CatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var (series, position) = SplitSeries(item.SeriesName);
        return new AudiobookMetadata(
            item.Asin ?? string.Empty,
            item.Title,
            item.Authors,
            series,
            position,
            AudibleMarkets.ToLanguageCode(item.Language),
            item.ImageUrl,
            item.Market,
            item.Narrators,
            item.IsCompleteEdition);
    }

    /// <summary>
    /// Splits the series of the catalog (<c>Dune, 1</c>) into its name and the position of the book.
    /// </summary>
    /// <param name="seriesName">The series of the catalog entry.</param>
    /// <returns>The name and the position, each <c>null</c> when absent.</returns>
    internal static (string? Name, string? Position) SplitSeries(string? seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            return (null, null);
        }

        var comma = seriesName.LastIndexOf(", ", StringComparison.Ordinal);
        if (comma > 0)
        {
            var position = seriesName[(comma + 2)..].Trim();
            if (decimal.TryParse(position, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
            {
                return (seriesName[..comma].Trim(), position);
            }
        }

        return (seriesName.Trim(), null);
    }
}
