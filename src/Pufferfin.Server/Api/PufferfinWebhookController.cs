using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Jellyfin.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Requests.Integration;

namespace Pufferfin.Server.Api;

/// <summary>
/// Receives the notifications Radarr and Sonarr send on each grab and import, so a title joins the library at once
/// and its request moves on without waiting for the next round.
/// </summary>
/// <remarks>
/// Radarr and Sonarr cannot hold a Jellyfin session: they authenticate with HTTP Basic, the password being a token
/// the server generated and wrote into their notification settings. The token never travels in a URL, so it cannot
/// end up in a request log. Without the right token, nothing happens.
/// </remarks>
[Route("Pufferfin/Requests/Webhook")]
[Tags("Pufferfin")]
[AllowAnonymous]
public class PufferfinWebhookController : BaseJellyfinApiController
{
    private readonly RequestStore _store;
    private readonly IRequestWaker _waker;
    private readonly IMediaLibrary _library;
    private readonly ILogger<PufferfinWebhookController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinWebhookController"/> class.
    /// </summary>
    /// <param name="store">The request store, which holds the token.</param>
    /// <param name="waker">Wakes the request processor.</param>
    /// <param name="library">The Jellyfin library.</param>
    /// <param name="logger">The logger.</param>
    public PufferfinWebhookController(RequestStore store, IRequestWaker waker, IMediaLibrary library, ILogger<PufferfinWebhookController> logger)
    {
        _store = store;
        _waker = waker;
        _library = library;
        _logger = logger;
    }

    /// <summary>
    /// Receives a Radarr or Sonarr notification.
    /// </summary>
    /// <param name="payload">The notification.</param>
    /// <response code="204">Notification received.</response>
    /// <response code="401">The token is missing or wrong.</response>
    /// <returns>No content.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult ReceivePufferfinArrNotification([FromBody] JsonObject? payload)
    {
        if (!HasValidToken())
        {
            return Unauthorized();
        }

        var eventType = payload?["eventType"]?.GetValue<string>();
        _logger.LogDebug("Pufferfin received a {EventType} notification", eventType);
        if (eventType is "Download" or "Import" or "ImportComplete")
        {
            // The file when the notification names it, else the folder of the title.
            var path = payload!["movieFile"]?["path"]?.GetValue<string>()
                ?? payload["episodeFile"]?["path"]?.GetValue<string>()
                ?? payload["movie"]?["folderPath"]?.GetValue<string>()
                ?? payload["series"]?["path"]?.GetValue<string>();
            _library.Refresh(path);
        }

        _waker.Wake();
        return NoContent();
    }

    private bool HasValidToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = credentials.IndexOf(':', StringComparison.Ordinal);
        var given = separator < 0 ? string.Empty : credentials[(separator + 1)..];
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(_store.GetWebhookToken()));
    }
}
