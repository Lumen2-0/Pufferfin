using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Seeding;

/// <summary>
/// The seeding policy: how long a torrent is guaranteed to seed, and when it may be purged.
/// </summary>
/// <param name="MinSeedingTime">The seeding time guaranteed to every torrent, private or not (<c>SEED_MIN_DAYS</c>).</param>
/// <param name="TargetRatio">The ratio from which a torrent past its minimum is purged (<c>SEED_TARGET_RATIO</c>), or <c>null</c>.</param>
/// <param name="MaxAge">The time after completion from which a torrent past its minimum is purged anyway (<c>SEED_MAX_DAYS</c>), or <c>null</c>.</param>
/// <param name="Categories">The qBittorrent categories the policy manages (<c>SEED_CATEGORIES</c>).</param>
public sealed record SeedPolicy(TimeSpan MinSeedingTime, decimal? TargetRatio, TimeSpan? MaxAge, IReadOnlySet<string> Categories)
{
    /// <summary>
    /// The minimum seeding time, in days, when <c>SEED_MIN_DAYS</c> is not set: a month, past the hit and run rules of
    /// most private trackers. When unsure, Pufferfin seeds longer.
    /// </summary>
    public const int DefaultMinDays = 30;

    /// <summary>
    /// Gets the categories managed when <c>SEED_CATEGORIES</c> is not set: the ones Pufferfin downloads into.
    /// </summary>
    public static IReadOnlyList<string> DefaultCategories { get; } = [MediaLayout.RadarrCategory, MediaLayout.SonarrCategory, MediaLayout.AudiobooksCategory];

    /// <summary>
    /// Reads the policy from the settings. Without a target ratio nor a maximum, nothing is purged by the policy:
    /// only the titles deleted from the interface are, once their minimum is reached.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The policy.</returns>
    public static SeedPolicy From(PufferfinSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var categories = settings.SeedCategories.Count > 0 ? settings.SeedCategories : DefaultCategories;
        return new SeedPolicy(
            TimeSpan.FromDays(settings.SeedMinDays ?? DefaultMinDays),
            settings.SeedTargetRatio,
            settings.SeedMaxDays is int maxDays ? TimeSpan.FromDays(maxDays) : null,
            categories.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets a value indicating whether the policy manages a category.
    /// </summary>
    /// <param name="category">The qBittorrent category.</param>
    /// <returns><c>true</c> when the policy manages it.</returns>
    public bool Manages(string? category) => category is not null && Categories.Contains(category);
}
