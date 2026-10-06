using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Gives Radarr or Sonarr a root folder for each category (standard, anime, documentary).
/// A category that already has a folder, Pufferfin's or one whose path names it, is left alone.
/// </summary>
public sealed class RootFoldersStep : IAutoConfigStep
{
    private readonly ArrClient _client;
    private readonly PufferfinService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="RootFoldersStep"/> class.
    /// </summary>
    /// <param name="client">The Radarr and Sonarr client.</param>
    /// <param name="service">Radarr or Sonarr.</param>
    public RootFoldersStep(ArrClient client, PufferfinService service)
    {
        if (service is not (PufferfinService.Radarr or PufferfinService.Sonarr))
        {
            throw new ArgumentOutOfRangeException(nameof(service), service, "Only Radarr and Sonarr have root folders.");
        }

        _client = client;
        _service = service;
    }

    /// <inheritdoc />
    public string Name => $"{_service} root folders";

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(_service) is not { } service)
        {
            return StepOutcomes.NotConfigured(_service.ToString());
        }

        var defaults = _service == PufferfinService.Radarr ? MediaLayout.RadarrRootFolders : MediaLayout.SonarrRootFolders;
        var created = new List<string>();
        try
        {
            var folders = await _client.GetRootFoldersAsync(service, cancellationToken).ConfigureAwait(false);
            foreach (var (category, path) in defaults)
            {
                if (MediaLayout.HasOwnFolder(category, defaults, folders))
                {
                    continue;
                }

                try
                {
                    await _client.AddRootFolderAsync(service, path, cancellationToken).ConfigureAwait(false);
                }
                catch (ArrApiException ex) when (ex.Failure == ArrFailure.Rejected)
                {
                    return new AutoConfigStepOutcome(
                        AutoConfigStepStatus.Failed,
                        $"{_service} refused the root folder {path}: check that it exists and is writable in the {_service} container.");
                }

                created.Add(path);
                folders.Add(new ArrRootFolder { Path = path });
            }
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex, created);
        }

        return StepOutcomes.FromCreated(created);
    }
}
