using System;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// A title an administrator asked to delete later, with the identity it had then, so the deletion can check it still
/// deletes that title.
/// </summary>
/// <param name="ItemId">The Jellyfin item id.</param>
/// <param name="Kind">The kind of the title.</param>
/// <param name="Title">The title, for the logs and the list.</param>
/// <param name="TmdbId">The TMDB id the title had, when known.</param>
/// <param name="TvdbId">The TVDB id the series had, when known.</param>
/// <param name="ScheduledAt">When it was scheduled.</param>
/// <param name="DueAt">When it is deleted.</param>
/// <param name="UserId">The administrator who scheduled it.</param>
public sealed record ScheduledDeletion(
    Guid ItemId,
    LibraryTitleKind Kind,
    string Title,
    int? TmdbId,
    int? TvdbId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset DueAt,
    Guid UserId)
{
    /// <summary>
    /// Gets what became of it, or <c>null</c> while it waits.
    /// </summary>
    public ScheduledDeletionOutcome? Outcome { get; init; }

    /// <summary>
    /// Gets when it was handled.
    /// </summary>
    public DateTimeOffset? HandledAt { get; init; }

    /// <summary>
    /// Gets why it needs an administrator, when it does.
    /// </summary>
    public string? Message { get; init; }
}
