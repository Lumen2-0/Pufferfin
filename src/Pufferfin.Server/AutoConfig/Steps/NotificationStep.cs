using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Declares Pufferfin as a webhook notification of Radarr or Sonarr, on every grab and import, so an imported title
/// joins the library at once and the Play button replaces the Download button by itself.
/// </summary>
/// <remarks>
/// The webhook authenticates with HTTP Basic, the password being the token of the request store. A notification
/// named <see cref="NotificationName"/> is never modified: to point it elsewhere, delete it and the next run recreates it.
/// </remarks>
public sealed class NotificationStep : IAutoConfigStep
{
    /// <summary>
    /// The name of the notification Pufferfin creates.
    /// </summary>
    public const string NotificationName = "Pufferfin";

    /// <summary>
    /// The user name sent with the webhook token.
    /// </summary>
    public const string WebhookUser = "pufferfin";

    /// <summary>
    /// The path of the webhook, relative to the server base URL.
    /// </summary>
    public const string WebhookPath = "Pufferfin/Requests/Webhook";

    private readonly ArrClient _client;
    private readonly PufferfinService _service;
    private readonly RequestStore _store;
    private readonly Func<Uri?> _serverUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationStep"/> class.
    /// </summary>
    /// <param name="client">The Radarr and Sonarr client.</param>
    /// <param name="service">Radarr or Sonarr.</param>
    /// <param name="store">The request store, which holds the webhook token.</param>
    /// <param name="serverUrl">Gives the URL at which Radarr and Sonarr reach this server.</param>
    public NotificationStep(ArrClient client, PufferfinService service, RequestStore store, Func<Uri?> serverUrl)
    {
        if (service is not (PufferfinService.Radarr or PufferfinService.Sonarr))
        {
            throw new ArgumentOutOfRangeException(nameof(service), service, "Only Radarr and Sonarr notify Pufferfin.");
        }

        _client = client;
        _service = service;
        _store = store;
        _serverUrl = serverUrl;
    }

    /// <inheritdoc />
    public string Name => $"{_service} notification to Pufferfin";

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(_service) is not { } service)
        {
            return StepOutcomes.NotConfigured(_service.ToString());
        }

        try
        {
            var notifications = await _client.GetItemsAsync(service, "notification", cancellationToken).ConfigureAwait(false);
            if (notifications.Any(n => string.Equals(n.Name, NotificationName, StringComparison.OrdinalIgnoreCase)))
            {
                return new AutoConfigStepOutcome(AutoConfigStepStatus.Unchanged);
            }

            if (_serverUrl() is not { } serverUrl)
            {
                return new AutoConfigStepOutcome(AutoConfigStepStatus.Skipped, "The address of this server is not known yet.");
            }

            var schema = await _client.GetSchemaAsync(service, "notification", cancellationToken).ConfigureAwait(false);
            if (ArrFields.Template(schema, "Webhook") is not { } notification)
            {
                return new AutoConfigStepOutcome(AutoConfigStepStatus.Failed, $"{_service} does not offer webhook notifications.");
            }

            notification["name"] = NotificationName;
            foreach (var trigger in new[] { "onGrab", "onDownload", "onUpgrade", "onImportComplete" })
            {
                if (notification.ContainsKey(trigger))
                {
                    notification[trigger] = true;
                }
            }

            ArrFields.Set(notification, "url", ArrHttpClient.Combine(serverUrl, WebhookPath).AbsoluteUri);
            ArrFields.Set(notification, "method", 1);
            ArrFields.Set(notification, "username", WebhookUser);
            ArrFields.Set(notification, "password", _store.GetWebhookToken());
            await _client.CreateItemAsync(service, "notification", notification, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex) when (ex.Failure == ArrFailure.Rejected)
        {
            return new AutoConfigStepOutcome(
                AutoConfigStepStatus.Failed,
                $"{_service} refused the notification to Pufferfin: check that {_service} reaches this server.");
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex);
        }

        return StepOutcomes.FromCreated(["the notification " + NotificationName]);
    }
}
