using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Declares qBittorrent as the download client of Radarr or Sonarr, with the category of the service.
/// A qBittorrent client already declared, by Pufferfin or by hand, is left as it is.
/// </summary>
public sealed class DownloadClientStep : IAutoConfigStep
{
    /// <summary>
    /// The name of the download client Pufferfin creates.
    /// </summary>
    public const string ClientName = "qBittorrent (Pufferfin)";

    private readonly ArrClient _client;
    private readonly PufferfinService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadClientStep"/> class.
    /// </summary>
    /// <param name="client">The Radarr and Sonarr client.</param>
    /// <param name="service">Radarr or Sonarr.</param>
    public DownloadClientStep(ArrClient client, PufferfinService service)
    {
        if (service is not (PufferfinService.Radarr or PufferfinService.Sonarr))
        {
            throw new ArgumentOutOfRangeException(nameof(service), service, "Only Radarr and Sonarr have download clients.");
        }

        _client = client;
        _service = service;
    }

    /// <inheritdoc />
    public string Name => $"{_service} download client";

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(_service) is not { } service)
        {
            return StepOutcomes.NotConfigured(_service.ToString());
        }

        if (settings.GetService(PufferfinService.QBittorrent) is not { BaseUrl: { } qBittorrentUrl } qBittorrent)
        {
            return StepOutcomes.NotConfigured("qBittorrent");
        }

        try
        {
            var clients = await _client.GetItemsAsync(service, "downloadclient", cancellationToken).ConfigureAwait(false);
            if (clients.Any(c => string.Equals(c.Implementation, "QBittorrent", StringComparison.OrdinalIgnoreCase)))
            {
                return new AutoConfigStepOutcome(AutoConfigStepStatus.Unchanged);
            }

            var schema = await _client.GetSchemaAsync(service, "downloadclient", cancellationToken).ConfigureAwait(false);
            if (ArrFields.Template(schema, "QBittorrent") is not { } template)
            {
                return new AutoConfigStepOutcome(AutoConfigStepStatus.Failed, $"{_service} does not offer qBittorrent as a download client.");
            }

            template["name"] = ClientName;
            template["enable"] = true;
            template["priority"] = 1;
            ArrFields.Set(template, "host", qBittorrentUrl.Host);
            ArrFields.Set(template, "port", qBittorrentUrl.Port);
            ArrFields.Set(template, "useSsl", qBittorrentUrl.Scheme == Uri.UriSchemeHttps);
            ArrFields.Set(template, "urlBase", qBittorrentUrl.AbsolutePath.Trim('/'));
            ArrFields.Set(template, "username", qBittorrent.Username ?? string.Empty);
            ArrFields.Set(template, "password", qBittorrent.Password ?? string.Empty);
            var category = _service == PufferfinService.Radarr ? MediaLayout.RadarrCategory : MediaLayout.SonarrCategory;
            ArrFields.Set(template, "movieCategory", category);
            ArrFields.Set(template, "tvCategory", category);

            await _client.CreateItemAsync(service, "downloadclient", template, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex) when (ex.Failure == ArrFailure.Rejected)
        {
            return new AutoConfigStepOutcome(
                AutoConfigStepStatus.Failed,
                $"{_service} refused the qBittorrent download client: check that {_service} reaches qBittorrent at the configured address.");
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex);
        }

        return StepOutcomes.FromCreated(["the download client " + ClientName]);
    }
}
