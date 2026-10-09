using System;
using System.ComponentModel.DataAnnotations;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// A new download request.
/// </summary>
public sealed class CreateMediaRequestDto
{
    /// <summary>Gets or sets what is asked: a film, a season, an episode or an audiobook. A book to read is refused.</summary>
    [Required]
    public RequestKind Kind { get; set; }

    /// <summary>Gets or sets the TMDB id of the film or of the series. Zero or omitted for an audiobook.</summary>
    [Range(0, int.MaxValue)]
    public int TmdbId { get; set; }

    /// <summary>Gets or sets the Audible ASIN of an audiobook.</summary>
    public string? Asin { get; set; }

    /// <summary>Gets or sets the Audible market the audiobook was found in (<c>fr</c>, <c>us</c>...), when known.</summary>
    public string? Market { get; set; }

    /// <summary>Gets or sets the TMDB season number, for a season or an episode.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the TMDB episode number, for an episode.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets the TMDB air date of an episode, used when the Sonarr numbering differs.</summary>
    public DateOnly? AirDate { get; set; }

    /// <summary>
    /// Gets or sets the audio language: an ISO 639-1 code, or <c>original</c>. When omitted: the language remembered for
    /// the series (the language of the book, for an audiobook or a book), else the first preferred language of the user, else of the server.
    /// </summary>
    public string? Language { get; set; }
}
