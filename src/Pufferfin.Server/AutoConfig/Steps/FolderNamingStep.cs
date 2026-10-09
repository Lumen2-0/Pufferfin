using System;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Puts the id of a title in the name of its folder, as the library reads it: <c>[tmdbid-…]</c> for a film of Radarr,
/// <c>[tvdbid-…]</c> for a series of Sonarr. A folder named after its title alone ("The Hunt (2012)") can be taken by
/// the library for another title of the same name; with the id it cannot. Only the folders created from now on get it:
/// Radarr and Sonarr never rename existing folders by themselves. A format that already names an id is left alone.
/// </summary>
public sealed class FolderNamingStep : IAutoConfigStep
{
    /// <summary>The folder format of the films of Radarr.</summary>
    public const string MovieFolderFormat = "{Movie Title} ({Release Year}) [tmdbid-{TmdbId}]";

    /// <summary>The folder format of the series of Sonarr.</summary>
    public const string SeriesFolderFormat = "{Series Title} ({Series Year}) [tvdbid-{TvdbId}]";

    private readonly ArrClient _client;
    private readonly PufferfinService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderNamingStep"/> class.
    /// </summary>
    /// <param name="client">The Radarr and Sonarr client.</param>
    /// <param name="service">Radarr or Sonarr.</param>
    public FolderNamingStep(ArrClient client, PufferfinService service)
    {
        if (service is not (PufferfinService.Radarr or PufferfinService.Sonarr))
        {
            throw new ArgumentOutOfRangeException(nameof(service), service, "Only Radarr and Sonarr name folders.");
        }

        _client = client;
        _service = service;
    }

    /// <inheritdoc />
    public string Name => $"{_service} folder names";

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(_service) is not { } service)
        {
            return StepOutcomes.NotConfigured(_service.ToString());
        }

        var isRadarr = _service == PufferfinService.Radarr;
        var field = isRadarr ? "movieFolderFormat" : "seriesFolderFormat";
        try
        {
            var naming = await _client.GetNamingAsync(service, cancellationToken).ConfigureAwait(false);
            var current = naming[field]?.GetValue<string>() ?? string.Empty;
            if (current.Contains("tmdb", StringComparison.OrdinalIgnoreCase)
                || current.Contains("tvdb", StringComparison.OrdinalIgnoreCase)
                || current.Contains("imdb", StringComparison.OrdinalIgnoreCase))
            {
                return new AutoConfigStepOutcome(AutoConfigStepStatus.Unchanged);
            }

            naming[field] = isRadarr ? MovieFolderFormat : SeriesFolderFormat;
            await _client.UpdateNamingAsync(service, naming, cancellationToken).ConfigureAwait(false);
            return new AutoConfigStepOutcome(AutoConfigStepStatus.Applied, $"The new folders of {_service} name the id of their title.");
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex);
        }
    }
}
