using System;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// A download request, as the client sees it. Also the payload of the <c>PufferfinRequestUpdated</c> WebSocket message.
/// </summary>
public sealed class MediaRequestDto
{
    /// <summary>Gets the id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the user who asked.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets what is asked.</summary>
    public required RequestKind Kind { get; init; }

    /// <summary>Gets the TMDB id of the film or of the series. Zero for an audiobook or a book to read.</summary>
    public required int TmdbId { get; init; }

    /// <summary>Gets the Audible ASIN of an audiobook.</summary>
    public string? Asin { get; init; }

    /// <summary>Gets the Shelfmark metadata provider of a book to read, on an old request: books to read were removed.</summary>
    public string? BookProvider { get; init; }

    /// <summary>Gets the id of a book to read in its metadata provider.</summary>
    public string? BookId { get; init; }

    /// <summary>Gets the TMDB season number.</summary>
    public int? SeasonNumber { get; init; }

    /// <summary>Gets the TMDB episode number.</summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>Gets the title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the requested audio language: an ISO 639-1 code, or <c>original</c>.</summary>
    public required string Language { get; init; }

    /// <summary>Gets the language of the release that was taken: an ISO 639-1 code, or <c>original</c>.</summary>
    public string? ChosenLanguage { get; init; }

    /// <summary>Gets the title of the release that was taken.</summary>
    public string? ReleaseTitle { get; init; }

    /// <summary>Gets the state.</summary>
    public required RequestState State { get; init; }

    /// <summary>Gets why the request failed or waits. Always set for a failed request.</summary>
    public RequestProblemDto? Problem { get; init; }

    /// <summary>Gets the download progress, from 0 to 1, while downloading.</summary>
    public double? Progress { get; init; }

    /// <summary>Gets the download speed in bytes per second, when known.</summary>
    public long? BytesPerSecond { get; init; }

    /// <summary>Gets the estimated time left in seconds, when known.</summary>
    public long? EtaSeconds { get; init; }

    /// <summary>Gets the one based place among the requests waiting for a slot.</summary>
    public int? QueuePosition { get; init; }

    /// <summary>Gets when the request was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets when the request last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Gets when the server searches again a request waiting for sources.</summary>
    public DateTimeOffset? NextAttemptAt { get; init; }

    /// <summary>
    /// Maps a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="queuePosition">The one based place among the requests waiting for a slot.</param>
    /// <returns>The DTO.</returns>
    public static MediaRequestDto From(MediaRequest request, int? queuePosition)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new MediaRequestDto
        {
            Id = request.Id,
            UserId = request.UserId,
            Kind = request.Kind,
            TmdbId = request.TmdbId,
            Asin = request.Asin,
            BookProvider = request.BookProvider,
            BookId = request.BookId,
            SeasonNumber = request.SeasonNumber,
            EpisodeNumber = request.EpisodeNumber,
            Title = request.Title,
            Language = request.Language,
            ChosenLanguage = request.ChosenLanguage,
            ReleaseTitle = request.ReleaseTitle,
            State = request.State,
            Problem = request.Problem is { } problem ? new RequestProblemDto { Code = problem.Code, Message = problem.Message, Detail = problem.Detail } : null,
            Progress = request.State == RequestState.Downloading ? request.Progress?.Fraction : null,
            BytesPerSecond = request.State == RequestState.Downloading ? request.Progress?.BytesPerSecond : null,
            EtaSeconds = request.State == RequestState.Downloading ? request.Progress?.EtaSeconds : null,
            QueuePosition = request.State == RequestState.Queued ? queuePosition : null,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.UpdatedAt,
            NextAttemptAt = request.State == RequestState.WaitingForSources ? request.NextAttemptAt : null
        };
    }
}
