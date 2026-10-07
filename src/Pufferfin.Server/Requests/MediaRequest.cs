using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pufferfin.Server.Requests;

/// <summary>
/// A download request: a title, a kind, a language and a user. Immutable: every change produces a new version.
/// </summary>
public sealed record MediaRequest
{
    /// <summary>
    /// The language value that asks for the original version only.
    /// </summary>
    public const string OriginalLanguage = "original";

    /// <summary>Gets the id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the user who asked.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets what is asked.</summary>
    public required RequestKind Kind { get; init; }

    /// <summary>Gets the TMDB id of the film or of the series. Zero for an audiobook or a book.</summary>
    public required int TmdbId { get; init; }

    /// <summary>Gets the Audible ASIN of an audiobook, upper case.</summary>
    public string? Asin { get; init; }

    /// <summary>Gets the Audible market the audiobook was found in.</summary>
    public string? Market { get; init; }

    /// <summary>Gets the Shelfmark metadata provider of a book to read (<c>hardcover</c>, <c>openlibrary</c>, <c>googlebooks</c>).</summary>
    public string? BookProvider { get; init; }

    /// <summary>Gets the id of a book to read in its metadata provider.</summary>
    public string? BookId { get; init; }

    /// <summary>Gets the season number (TMDB numbering) of a season or an episode.</summary>
    public int? SeasonNumber { get; init; }

    /// <summary>Gets the episode number (TMDB numbering) of an episode.</summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>Gets the air date of an episode, used when the Sonarr numbering differs.</summary>
    public DateOnly? AirDate { get; init; }

    /// <summary>Gets the title, for display.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the requested audio language: an ISO 639-1 code, or <see cref="OriginalLanguage"/>.</summary>
    public required string Language { get; init; }

    /// <summary>Gets the state.</summary>
    public required RequestState State { get; init; }

    /// <summary>Gets why the request failed or waits. Always set when <see cref="State"/> is <see cref="RequestState.Failed"/>.</summary>
    public RequestProblem? Problem { get; init; }

    /// <summary>Gets the language of the release that was taken: an ISO 639-1 code, or <see cref="OriginalLanguage"/>.</summary>
    public string? ChosenLanguage { get; init; }

    /// <summary>Gets the title of the release that was taken.</summary>
    public string? ReleaseTitle { get; init; }

    /// <summary>Gets the Radarr film id or the Sonarr series id.</summary>
    public int? ArrId { get; init; }

    /// <summary>Gets the TVDB id of a series.</summary>
    public int? TvdbId { get; init; }

    /// <summary>Gets the id of the Shelfmark download of an audiobook or a book (the source id of the release taken).</summary>
    public string? DownloadId { get; init; }

    /// <summary>Gets the source ids of the Shelfmark releases whose download failed, never taken again for this request.</summary>
    public IReadOnlyList<string> ExcludedReleases { get; init; } = [];

    /// <summary>Gets the folder an audiobook or a book was filed in, once imported.</summary>
    public string? LibraryPath { get; init; }

    /// <summary>Gets the hash of the qBittorrent torrent the files of an imported audiobook or book are hard links of, when found.</summary>
    public string? TorrentHash { get; init; }

    /// <summary>Gets the Sonarr episodes covered by the request.</summary>
    public IReadOnlyList<RequestEpisode> Episodes { get; init; } = [];

    /// <summary>Gets the queue order: lower comes first.</summary>
    public long Position { get; init; }

    /// <summary>Gets the number of searches run so far.</summary>
    public int Attempts { get; init; }

    /// <summary>Gets when the request was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when the request last changed state or problem.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Gets when Pufferfin grabbed a release for this request.</summary>
    public DateTimeOffset? GrabbedAt { get; init; }

    /// <summary>Gets when the server searches again a request waiting for sources.</summary>
    public DateTimeOffset? NextAttemptAt { get; init; }

    /// <summary>Gets the live download progress, which is not persisted.</summary>
    [JsonIgnore]
    public RequestProgress? Progress { get; init; }

    /// <summary>Gets the version, incremented on every save, so a stale writer cannot overwrite a newer state.</summary>
    public int Version { get; init; }

    /// <summary>Gets a value indicating whether the request is still on its way: neither available, failed, cancelled nor refused.</summary>
    [JsonIgnore]
    public bool IsActive => State is not (RequestState.Available or RequestState.Failed or RequestState.Cancelled or RequestState.Rejected);

    /// <summary>Gets a value indicating whether the request takes one of the <c>MAX_CONCURRENT_DOWNLOADS</c> slots.</summary>
    [JsonIgnore]
    public bool TakesSlot => State is RequestState.Searching or RequestState.Downloading;
}
