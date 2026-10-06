using System;

namespace Pufferfin.Catalog.Letterboxd;

/// <summary>
/// A film logged in a Letterboxd diary.
/// </summary>
public sealed class LetterboxdDiaryEntry
{
    /// <summary>
    /// Gets the TMDB id of the film.
    /// </summary>
    public required int TmdbId { get; init; }

    /// <summary>
    /// Gets the film title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the rating out of 5, when the member rated the film.
    /// </summary>
    public double? Rating { get; init; }

    /// <summary>
    /// Gets the date the film was watched.
    /// </summary>
    public DateOnly? WatchedDate { get; init; }
}
