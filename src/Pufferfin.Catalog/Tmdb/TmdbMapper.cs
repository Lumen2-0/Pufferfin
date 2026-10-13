using System;
using System.Globalization;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// Converts TMDB payloads into catalog entries.
/// </summary>
internal static class TmdbMapper
{
    /// <summary>
    /// Builds the stable key of a TMDB entry.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="tmdbId">The TMDB id.</param>
    /// <returns>The key, for example <c>tmdb:movie:603</c>.</returns>
    public static string Key(CatalogItemKind kind, int tmdbId)
        => string.Create(CultureInfo.InvariantCulture, $"tmdb:{kind.ToString().ToLowerInvariant()}:{tmdbId}");

    /// <summary>
    /// Reads the kind of a TMDB entry.
    /// </summary>
    /// <param name="mediaType">The TMDB <c>media_type</c>.</param>
    /// <returns>The kind, or <c>null</c> for an unsupported type.</returns>
    public static CatalogItemKind? ParseKind(string? mediaType) => mediaType switch
    {
        "movie" => CatalogItemKind.Movie,
        "tv" => CatalogItemKind.Series,
        "person" => CatalogItemKind.Person,
        _ => null
    };

    /// <summary>
    /// Parses a TMDB date (<c>yyyy-MM-dd</c>, possibly empty).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The date, or <c>null</c>.</returns>
    public static DateOnly? ParseDate(string? value)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>
    /// Converts a search, credit or recommendation entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="kind">The kind when the payload has no <c>media_type</c> (recommendations, collection parts).</param>
    /// <returns>The catalog entry, or <c>null</c> for an unsupported type.</returns>
    public static CatalogItem? ToItem(TmdbModels.Entry entry, CatalogItemKind? kind = null)
    {
        var resolvedKind = ParseKind(entry.MediaType) ?? kind;
        if (resolvedKind is null || entry.Id <= 0)
        {
            return null;
        }

        var isMovie = resolvedKind == CatalogItemKind.Movie;
        var title = isMovie ? entry.Title : entry.Name;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var isPerson = resolvedKind == CatalogItemKind.Person;
        return new CatalogItem
        {
            Key = Key(resolvedKind.Value, entry.Id),
            Kind = resolvedKind.Value,
            Title = title,
            OriginalTitle = isMovie ? entry.OriginalTitle : (isPerson ? null : entry.OriginalName),
            Year = ParseDate(isMovie ? entry.ReleaseDate : entry.FirstAirDate)?.Year,
            Overview = string.IsNullOrWhiteSpace(entry.Overview) ? null : entry.Overview,
            ImageUrl = TmdbClient.ImageUrl(isPerson ? entry.ProfilePath : entry.PosterPath, isPerson ? "w185" : "w500"),
            BackdropUrl = TmdbClient.ImageUrl(entry.BackdropPath, "w1280"),
            Availability = CatalogAvailability.Missing,
            TmdbId = entry.Id,
            KnownForDepartment = isPerson ? entry.KnownForDepartment : null,
            Role = !string.IsNullOrWhiteSpace(entry.Character) ? entry.Character : entry.Job,
            GenreIds = isPerson ? [] : entry.GenreIds,
            Sources = [CatalogSource.Tmdb]
        };
    }
}
