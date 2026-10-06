using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// The keys of the Pufferfin configuration store and their definitions.
/// A key is both the name of the environment variable that seeds it and the identifier of the field.
/// </summary>
public static class SettingKeys
{
    /// <summary>Radarr base URL.</summary>
    public const string RadarrUrl = "RADARR_URL";

    /// <summary>Radarr API key.</summary>
    public const string RadarrApiKey = "RADARR_API_KEY";

    /// <summary>Sonarr base URL.</summary>
    public const string SonarrUrl = "SONARR_URL";

    /// <summary>Sonarr API key.</summary>
    public const string SonarrApiKey = "SONARR_API_KEY";

    /// <summary>Prowlarr base URL.</summary>
    public const string ProwlarrUrl = "PROWLARR_URL";

    /// <summary>Prowlarr API key.</summary>
    public const string ProwlarrApiKey = "PROWLARR_API_KEY";

    /// <summary>qBittorrent Web UI base URL.</summary>
    public const string QBittorrentUrl = "QBITTORRENT_URL";

    /// <summary>qBittorrent Web UI user name.</summary>
    public const string QBittorrentUsername = "QBITTORRENT_USERNAME";

    /// <summary>qBittorrent Web UI password.</summary>
    public const string QBittorrentPassword = "QBITTORRENT_PASSWORD";

    /// <summary>Bazarr base URL.</summary>
    public const string BazarrUrl = "BAZARR_URL";

    /// <summary>Bazarr API key.</summary>
    public const string BazarrApiKey = "BAZARR_API_KEY";

    /// <summary>Shelfmark base URL.</summary>
    public const string ShelfmarkUrl = "SHELFMARK_URL";

    /// <summary>TMDB API key (v3 key or v4 read access token).</summary>
    public const string TmdbApiKey = "TMDB_API_KEY";

    /// <summary>TMDB search and sheet languages, in order of preference.</summary>
    public const string TmdbLanguages = "TMDB_LANGUAGES";

    /// <summary>FlareSolverr base URL.</summary>
    public const string FlareSolverrUrl = "FLARESOLVERR_URL";

    /// <summary>Preferred audio languages, in order of preference.</summary>
    public const string PreferredAudioLanguages = "PREFERRED_AUDIO_LANGUAGES";

    /// <summary>Audible markets used for the audiobook catalog.</summary>
    public const string AudibleMarkets = "AUDIBLE_MARKETS";

    /// <summary>Whether a release that only has subtitles in the wanted language is rejected.</summary>
    public const string RejectSubtitledOnly = "REJECT_SUBTITLED_ONLY";

    /// <summary>Minimum number of days a torrent is seeded.</summary>
    public const string SeedMinDays = "SEED_MIN_DAYS";

    /// <summary>Maximum number of days a torrent is seeded.</summary>
    public const string SeedMaxDays = "SEED_MAX_DAYS";

    /// <summary>Ratio after which a torrent may be purged, once the minimum seeding time is reached.</summary>
    public const string SeedTargetRatio = "SEED_TARGET_RATIO";

    /// <summary>qBittorrent categories the seeding policy manages. Torrents in other categories are never touched.</summary>
    public const string SeedCategories = "SEED_CATEGORIES";

    /// <summary>Maximum number of simultaneous downloads.</summary>
    public const string MaxConcurrentDownloads = "MAX_CONCURRENT_DOWNLOADS";

    /// <summary>The library directory, as this server sees it.</summary>
    public const string MediaPath = "MEDIA_PATH";

    /// <summary>The downloads directory, as this server sees it.</summary>
    public const string DownloadsPath = "DOWNLOADS_PATH";

    /// <summary>The downloads directory, as qBittorrent sees it, when it differs from <see cref="DownloadsPath"/>.</summary>
    public const string QBittorrentDownloadsPath = "QBITTORRENT_DOWNLOADS_PATH";

    /// <summary>Disk usage, in percent, from which the storage screen warns.</summary>
    public const string StorageWarningPercent = "STORAGE_WARNING_PERCENT";

    private static readonly SettingDefinition[] _all =
    [
        new(RadarrUrl, SettingKind.Url, PufferfinService.Radarr),
        new(RadarrApiKey, SettingKind.Secret, PufferfinService.Radarr),
        new(SonarrUrl, SettingKind.Url, PufferfinService.Sonarr),
        new(SonarrApiKey, SettingKind.Secret, PufferfinService.Sonarr),
        new(ProwlarrUrl, SettingKind.Url, PufferfinService.Prowlarr),
        new(ProwlarrApiKey, SettingKind.Secret, PufferfinService.Prowlarr),
        new(QBittorrentUrl, SettingKind.Url, PufferfinService.QBittorrent),
        new(QBittorrentUsername, SettingKind.Text, PufferfinService.QBittorrent),
        new(QBittorrentPassword, SettingKind.Secret, PufferfinService.QBittorrent),
        new(BazarrUrl, SettingKind.Url, PufferfinService.Bazarr),
        new(BazarrApiKey, SettingKind.Secret, PufferfinService.Bazarr),
        new(ShelfmarkUrl, SettingKind.Url, PufferfinService.Shelfmark),
        new(TmdbApiKey, SettingKind.Secret, PufferfinService.Tmdb),
        new(TmdbLanguages, SettingKind.LanguageList),
        new(FlareSolverrUrl, SettingKind.Url, PufferfinService.FlareSolverr),
        new(PreferredAudioLanguages, SettingKind.LanguageList),
        new(AudibleMarkets, SettingKind.TextList),
        new(RejectSubtitledOnly, SettingKind.Boolean),
        new(SeedMinDays, SettingKind.Integer, Minimum: 0),
        new(SeedMaxDays, SettingKind.Integer, Minimum: 0),
        new(SeedTargetRatio, SettingKind.Decimal, Minimum: 0),
        new(SeedCategories, SettingKind.TextList),
        new(MaxConcurrentDownloads, SettingKind.Integer, Minimum: 1),
        new(MediaPath, SettingKind.Path),
        new(DownloadsPath, SettingKind.Path),
        new(QBittorrentDownloadsPath, SettingKind.Path, PufferfinService.QBittorrent),
        new(StorageWarningPercent, SettingKind.Integer, Minimum: 1, Maximum: 100),
    ];

    private static readonly FrozenDictionary<string, SettingDefinition> _byKey = _all.ToFrozenDictionary(d => d.Key, StringComparer.Ordinal);

    /// <summary>
    /// Gets all setting definitions, in display order.
    /// </summary>
    public static IReadOnlyList<SettingDefinition> All => _all;

    /// <summary>
    /// Looks up a definition by key. Keys are case sensitive, like environment variables on Linux.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="definition">The definition, when found.</param>
    /// <returns><c>true</c> when the key is known.</returns>
    public static bool TryGet(string key, out SettingDefinition definition)
        => _byKey.TryGetValue(key, out definition!);

    /// <summary>
    /// Gets a definition by key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The definition.</returns>
    public static SettingDefinition Get(string key) => _byKey[key];
}
