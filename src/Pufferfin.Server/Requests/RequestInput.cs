using System;

namespace Pufferfin.Server.Requests;

/// <summary>
/// What a user asks for.
/// </summary>
/// <param name="Kind">A film, a season, an episode, an audiobook or a book to read.</param>
/// <param name="TmdbId">The TMDB id of the film or of the series. Zero for an audiobook or a book.</param>
/// <param name="SeasonNumber">The TMDB season number of a season or an episode.</param>
/// <param name="EpisodeNumber">The TMDB episode number of an episode.</param>
/// <param name="AirDate">The TMDB air date of an episode, used when the Sonarr numbering differs.</param>
/// <param name="Language">The audio language: an ISO 639-1 code, <c>original</c>, or <c>null</c> for the language remembered for the series (the language of the book, for an audiobook or a book), else the first preferred language.</param>
/// <param name="Asin">The Audible ASIN of an audiobook.</param>
/// <param name="Market">The Audible market the audiobook was found in, when known.</param>
/// <param name="BookProvider">The Shelfmark metadata provider of a book to read.</param>
/// <param name="BookId">The id of a book to read in its metadata provider.</param>
public sealed record RequestInput(
    RequestKind Kind,
    int TmdbId,
    int? SeasonNumber = null,
    int? EpisodeNumber = null,
    DateOnly? AirDate = null,
    string? Language = null,
    string? Asin = null,
    string? Market = null,
    string? BookProvider = null,
    string? BookId = null);
