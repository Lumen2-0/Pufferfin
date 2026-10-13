using System;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// The rules of the scheduled deletions: when one is due, and whether the title found is still the one scheduled.
/// </summary>
public static class ScheduledDeletions
{
    /// <summary>
    /// How long after it is scheduled a title is deleted.
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromDays(7);

    /// <summary>
    /// Schedules a title.
    /// </summary>
    /// <param name="title">The title, as the library has it now.</param>
    /// <param name="userId">The administrator.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The scheduled deletion, due <see cref="Delay"/> from now.</returns>
    public static ScheduledDeletion Schedule(LibraryTitle title, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(title);
        return new ScheduledDeletion(title.ItemId, title.Kind, title.Title, title.TmdbId, title.TvdbId, now, now + Delay, userId);
    }

    /// <summary>
    /// Tells what a run does with a scheduled deletion.
    /// </summary>
    /// <param name="deletion">The scheduled deletion.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The decision.</returns>
    public static ScheduledDeletionDecision Decide(ScheduledDeletion deletion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        if (deletion.Outcome is not null)
        {
            return ScheduledDeletionDecision.Done;
        }

        return now >= deletion.DueAt ? ScheduledDeletionDecision.Delete : ScheduledDeletionDecision.NotDue;
    }

    /// <summary>
    /// Says why the title about to be deleted is not the one scheduled.
    /// </summary>
    /// <param name="deletion">The scheduled deletion.</param>
    /// <param name="found">The title the deletion found under the same item id.</param>
    /// <returns>Why it must not be deleted, or <c>null</c> when it is the same title.</returns>
    public static string? Mismatch(ScheduledDeletion deletion, LibraryTitle found)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        ArgumentNullException.ThrowIfNull(found);
        if (!found.ItemId.Equals(deletion.ItemId))
        {
            return "the library item is not the one scheduled";
        }

        if (found.Kind != deletion.Kind)
        {
            return "the library item is no longer of the kind scheduled";
        }

        if (deletion.TmdbId is int tmdb && found.TmdbId != tmdb)
        {
            return "the library item no longer has the TMDB id scheduled";
        }

        if (deletion.TvdbId is int tvdb && found.TvdbId != tvdb)
        {
            return "the library item no longer has the TVDB id scheduled";
        }

        return null;
    }
}
