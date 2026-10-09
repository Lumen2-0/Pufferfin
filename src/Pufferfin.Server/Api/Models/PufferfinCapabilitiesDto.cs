using System;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// Which services are configured and enabled. The client shows or hides features from it, without a restart.
/// Contains only booleans: no URL, no key.
/// </summary>
public sealed class PufferfinCapabilitiesDto
{
    /// <summary>Gets a value indicating whether Radarr is enabled.</summary>
    public required bool Radarr { get; init; }

    /// <summary>Gets a value indicating whether Sonarr is enabled.</summary>
    public required bool Sonarr { get; init; }

    /// <summary>Gets a value indicating whether Prowlarr is enabled.</summary>
    public required bool Prowlarr { get; init; }

    /// <summary>Gets a value indicating whether qBittorrent is enabled.</summary>
    public required bool QBittorrent { get; init; }

    /// <summary>Gets a value indicating whether Bazarr is enabled.</summary>
    public required bool Bazarr { get; init; }

    /// <summary>Gets a value indicating whether Shelfmark is enabled.</summary>
    public required bool Shelfmark { get; init; }

    /// <summary>Gets a value indicating whether TMDB is enabled.</summary>
    public required bool Tmdb { get; init; }

    /// <summary>Gets a value indicating whether FlareSolverr is enabled.</summary>
    public required bool FlareSolverr { get; init; }

    /// <summary>
    /// Creates the DTO from the settings.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The DTO.</returns>
    public static PufferfinCapabilitiesDto From(PufferfinSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new PufferfinCapabilitiesDto
        {
            Radarr = settings.IsEnabled(PufferfinService.Radarr),
            Sonarr = settings.IsEnabled(PufferfinService.Sonarr),
            Prowlarr = settings.IsEnabled(PufferfinService.Prowlarr),
            QBittorrent = settings.IsEnabled(PufferfinService.QBittorrent),
            Bazarr = settings.IsEnabled(PufferfinService.Bazarr),
            Shelfmark = settings.IsEnabled(PufferfinService.Shelfmark),
            Tmdb = settings.IsEnabled(PufferfinService.Tmdb),
            FlareSolverr = settings.IsEnabled(PufferfinService.FlareSolverr)
        };
    }
}
