using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// An immutable, strongly typed view of the effective configuration.
/// </summary>
public sealed class PufferfinSettings
{
    private readonly IReadOnlyDictionary<string, SettingState> _states;
    private readonly Dictionary<PufferfinService, ServiceSettings> _services = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinSettings"/> class.
    /// </summary>
    /// <param name="states">The setting states, by key. Missing keys are unset.</param>
    public PufferfinSettings(IReadOnlyDictionary<string, SettingState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        _states = states;

        AddUrlAndKey(PufferfinService.Radarr, SettingKeys.RadarrUrl, SettingKeys.RadarrApiKey);
        AddUrlAndKey(PufferfinService.Sonarr, SettingKeys.SonarrUrl, SettingKeys.SonarrApiKey);
        AddUrlAndKey(PufferfinService.Prowlarr, SettingKeys.ProwlarrUrl, SettingKeys.ProwlarrApiKey);
        AddUrlAndKey(PufferfinService.Bazarr, SettingKeys.BazarrUrl, SettingKeys.BazarrApiKey);

        // qBittorrent can authenticate local clients without credentials: the URL is enough.
        if (GetUrl(SettingKeys.QBittorrentUrl) is { } qBittorrentUrl)
        {
            _services[PufferfinService.QBittorrent] = new ServiceSettings(
                PufferfinService.QBittorrent,
                qBittorrentUrl,
                username: GetText(SettingKeys.QBittorrentUsername),
                password: GetText(SettingKeys.QBittorrentPassword));
        }

        // Shelfmark can run without authentication: the URL is enough, the key is sent when set.
        if (GetUrl(SettingKeys.ShelfmarkUrl) is { } shelfmarkUrl)
        {
            _services[PufferfinService.Shelfmark] = new ServiceSettings(PufferfinService.Shelfmark, shelfmarkUrl, GetText(SettingKeys.ShelfmarkApiKey));
        }

        if (GetUrl(SettingKeys.FlareSolverrUrl) is { } flareSolverrUrl)
        {
            _services[PufferfinService.FlareSolverr] = new ServiceSettings(PufferfinService.FlareSolverr, flareSolverrUrl);
        }

        if (GetText(SettingKeys.TmdbApiKey) is { } tmdbApiKey)
        {
            _services[PufferfinService.Tmdb] = new ServiceSettings(PufferfinService.Tmdb, null, tmdbApiKey);
        }

        PreferredAudioLanguages = SettingValueParser.SplitList(GetText(SettingKeys.PreferredAudioLanguages));
        TmdbLanguages = SettingValueParser.SplitList(GetText(SettingKeys.TmdbLanguages));
        AudibleMarkets = SettingValueParser.SplitList(GetText(SettingKeys.AudibleMarkets));
        RejectSubtitledOnly = GetText(SettingKeys.RejectSubtitledOnly) is { } reject ? reject == "true" : null;
        SeedMinDays = GetInteger(SettingKeys.SeedMinDays);
        SeedMaxDays = GetInteger(SettingKeys.SeedMaxDays);
        SeedTargetRatio = GetText(SettingKeys.SeedTargetRatio) is { } ratio ? decimal.Parse(ratio, CultureInfo.InvariantCulture) : null;
        MaxConcurrentDownloads = GetInteger(SettingKeys.MaxConcurrentDownloads);
        SeedCategories = SettingValueParser.SplitList(GetText(SettingKeys.SeedCategories));
        MediaPath = GetText(SettingKeys.MediaPath);
        DownloadsPath = GetText(SettingKeys.DownloadsPath);
        QBittorrentDownloadsPath = GetText(SettingKeys.QBittorrentDownloadsPath);
        StorageWarningPercent = GetInteger(SettingKeys.StorageWarningPercent);
    }

    /// <summary>
    /// Gets the qBittorrent categories the seeding policy manages. Empty when unset.
    /// </summary>
    public IReadOnlyList<string> SeedCategories { get; }

    /// <summary>
    /// Gets the library directory as this server sees it, or <c>null</c> when unset.
    /// </summary>
    public string? MediaPath { get; }

    /// <summary>
    /// Gets the downloads directory as this server sees it, or <c>null</c> when unset.
    /// </summary>
    public string? DownloadsPath { get; }

    /// <summary>
    /// Gets the downloads directory as qBittorrent sees it, or <c>null</c> when unset.
    /// </summary>
    public string? QBittorrentDownloadsPath { get; }

    /// <summary>
    /// Gets the disk usage, in percent, from which the storage screen warns, or <c>null</c> when unset.
    /// </summary>
    public int? StorageWarningPercent { get; }

    /// <summary>
    /// Gets the preferred audio languages, in order. Empty when unset.
    /// </summary>
    public IReadOnlyList<string> PreferredAudioLanguages { get; }

    /// <summary>
    /// Gets the TMDB languages, in order. Empty when unset.
    /// </summary>
    public IReadOnlyList<string> TmdbLanguages { get; }

    /// <summary>
    /// Gets the Audible markets. Empty when unset.
    /// </summary>
    public IReadOnlyList<string> AudibleMarkets { get; }

    /// <summary>
    /// Gets whether subtitled only releases are rejected, or <c>null</c> when unset.
    /// </summary>
    public bool? RejectSubtitledOnly { get; }

    /// <summary>
    /// Gets the minimum seeding time in days, or <c>null</c> when unset.
    /// </summary>
    public int? SeedMinDays { get; }

    /// <summary>
    /// Gets the maximum seeding time in days, or <c>null</c> when unset.
    /// </summary>
    public int? SeedMaxDays { get; }

    /// <summary>
    /// Gets the target seeding ratio, or <c>null</c> when unset.
    /// </summary>
    public decimal? SeedTargetRatio { get; }

    /// <summary>
    /// Gets the maximum number of simultaneous downloads, or <c>null</c> when unset.
    /// </summary>
    public int? MaxConcurrentDownloads { get; }

    /// <summary>
    /// Gets a value indicating whether a service is configured, hence enabled.
    /// An unconfigured service is disabled, not in error.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <returns><c>true</c> when the service is enabled.</returns>
    public bool IsEnabled(PufferfinService service) => _services.ContainsKey(service);

    /// <summary>
    /// Gets the connection settings of a service.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <returns>The settings, or <c>null</c> when the service is not configured.</returns>
    public ServiceSettings? GetService(PufferfinService service) => _services.GetValueOrDefault(service);

    /// <inheritdoc />
    public override string ToString() => nameof(PufferfinSettings);

    private void AddUrlAndKey(PufferfinService service, string urlKey, string apiKeyKey)
    {
        if (GetUrl(urlKey) is { } url && GetText(apiKeyKey) is { } apiKey)
        {
            _services[service] = new ServiceSettings(service, url, apiKey);
        }
    }

    private string? GetText(string key) => _states.TryGetValue(key, out var state) ? state.Value : null;

    private Uri? GetUrl(string key) => GetText(key) is { } url ? new Uri(url, UriKind.Absolute) : null;

    private int? GetInteger(string key) => GetText(key) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : null;
}
