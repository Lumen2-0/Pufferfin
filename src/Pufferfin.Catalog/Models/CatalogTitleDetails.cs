using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// The detailed sheet of a film or a series.
/// </summary>
public sealed class CatalogTitleDetails
{
    /// <summary>
    /// Gets the kind: <see cref="CatalogItemKind.Movie"/> or <see cref="CatalogItemKind.Series"/>.
    /// </summary>
    public required CatalogItemKind Kind { get; init; }

    /// <summary>
    /// Gets the TMDB id.
    /// </summary>
    public required int TmdbId { get; init; }

    /// <summary>
    /// Gets the title, in the first configured language that has one, or the original title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the original title.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>
    /// Gets the ISO 639-1 original language.
    /// </summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>
    /// Gets the ISO 639-1 language the overview was taken from.
    /// </summary>
    public string? OverviewLanguage { get; init; }

    /// <summary>
    /// Gets the overview, in the first configured language that has one, or in the original language.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets the tagline.
    /// </summary>
    public string? Tagline { get; init; }

    /// <summary>
    /// Gets the release date (first air date for a series).
    /// </summary>
    public DateOnly? ReleaseDate { get; init; }

    /// <summary>
    /// Gets the release year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the runtime in minutes (episode runtime for a series).
    /// </summary>
    public int? RuntimeMinutes { get; init; }

    /// <summary>
    /// Gets the number of seasons of a series.
    /// </summary>
    public int? NumberOfSeasons { get; init; }

    /// <summary>
    /// Gets the TMDB vote average.
    /// </summary>
    public double? VoteAverage { get; init; }

    /// <summary>
    /// Gets the poster URL.
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>
    /// Gets the backdrop URL.
    /// </summary>
    public string? BackdropUrl { get; init; }

    /// <summary>
    /// Gets the IMDb id.
    /// </summary>
    public string? ImdbId { get; init; }

    /// <summary>
    /// Gets the TVDB id of a series.
    /// </summary>
    public int? TvdbId { get; init; }

    /// <summary>
    /// Gets the genres.
    /// </summary>
    public IReadOnlyList<CatalogGenre> Genres { get; init; } = [];

    /// <summary>
    /// Gets the countries of origin (production countries when TMDB gives no origin).
    /// </summary>
    public IReadOnlyList<CatalogCountry> Countries { get; init; } = [];

    /// <summary>
    /// Gets the main cast, in billing order.
    /// </summary>
    public IReadOnlyList<CatalogCredit> Cast { get; init; } = [];

    /// <summary>
    /// Gets the key crew members (directors, writers, creators).
    /// </summary>
    public IReadOnlyList<CatalogCredit> Crew { get; init; } = [];

    /// <summary>
    /// Gets the trailers and teasers, preferred languages first.
    /// </summary>
    public IReadOnlyList<CatalogVideo> Trailers { get; init; } = [];

    /// <summary>
    /// Gets the collection (saga) of a film.
    /// </summary>
    public CatalogCollectionSummary? Collection { get; init; }

    /// <summary>
    /// Gets the "more like this" recommendations, with their availability.
    /// </summary>
    public IReadOnlyList<CatalogItem> Recommendations { get; init; } = [];

    /// <summary>
    /// Gets whether the title is on disk.
    /// </summary>
    public required CatalogAvailability Availability { get; init; }

    /// <summary>
    /// Gets the id of the local library item when the title is on disk.
    /// </summary>
    public Guid? LocalItemId { get; init; }
}
