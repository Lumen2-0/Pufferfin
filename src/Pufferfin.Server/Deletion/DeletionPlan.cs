using System.Collections.Generic;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// What deleting a title will do, before anything is done.
/// </summary>
/// <param name="Title">The title.</param>
/// <param name="Mode">The mode.</param>
/// <param name="Torrents">What happens to each torrent of the title.</param>
/// <param name="PrivateBeforeMinimum">The number of private torrents (or of unknown privacy) that have not seeded their minimum yet.</param>
/// <param name="Warning">What the user must read before confirming, for the immediate mode.</param>
public sealed record DeletionPlan(LibraryTitle Title, DeletionMode Mode, IReadOnlyList<TorrentPlan> Torrents, int PrivateBeforeMinimum, string? Warning);
