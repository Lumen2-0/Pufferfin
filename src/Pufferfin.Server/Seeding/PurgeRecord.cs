using System;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// A torrent removed by Pufferfin, with its files.
/// </summary>
/// <param name="Hash">The torrent hash, lower case.</param>
/// <param name="Title">The title, for display.</param>
/// <param name="At">When it was removed.</param>
/// <param name="Reason">Why: the verdict of the policy, or <see cref="PurgeReason.ImmediateDeletion"/>.</param>
public sealed record PurgeRecord(string Hash, string Title, DateTimeOffset At, PurgeReason Reason);
