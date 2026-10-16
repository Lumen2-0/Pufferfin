using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Api.Models;

namespace Pufferfin.Server.Requests.Integration;

/// <summary>
/// Pushes every change of a request, progress included, to the sessions of its owner and of the administrators,
/// through the Jellyfin WebSocket messages (<see cref="SessionMessageType.PufferfinRequestUpdated"/>).
/// </summary>
public sealed class RequestUpdatesPublisher : IHostedService
{
    private const string WebClientName = "Jellyfin Web";

    private readonly RequestStore _store;
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<RequestUpdatesPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestUpdatesPublisher"/> class.
    /// </summary>
    /// <param name="store">The request store.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="logger">The logger.</param>
    public RequestUpdatesPublisher(RequestStore store, ISessionManager sessionManager, IUserManager userManager, ILogger<RequestUpdatesPublisher> logger)
    {
        _store = store;
        _sessionManager = sessionManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _store.Changed += OnChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _store.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, MediaRequest request)
        => _ = PublishAsync(request);

    private async Task PublishAsync(MediaRequest request)
    {
        try
        {
            var queued = _store.GetAll().Where(r => r.State == RequestState.Queued).Select(r => r.Id).ToList();
            var position = queued.FindIndex(id => id.Equals(request.Id));
            var dto = MediaRequestDto.From(request, position < 0 ? null : position + 1);
            var recipients = _userManager.GetUsers()
                .Where(u => u.HasPermission(PermissionKind.IsAdministrator))
                .Select(u => u.Id)
                .Append(request.UserId)
                .Distinct()
                .ToList();
            // Only the web client knows this message: the SDK of the native clients (Android TV) crashes on an unknown type.
            var devices = _sessionManager.Sessions
                .Where(s => recipients.Contains(s.UserId) && string.Equals(s.Client, WebClientName, StringComparison.Ordinal))
                .Select(s => s.DeviceId)
                .Distinct()
                .ToList();
            foreach (var deviceId in devices)
            {
                await _sessionManager.SendMessageToUserDeviceSessions(deviceId, SessionMessageType.PufferfinRequestUpdated, dto, CancellationToken.None).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // A session that cannot be reached must not break the request processing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogDebug("Pushing the update of request {RequestId} failed ({ErrorType})", request.Id, ex.GetType().Name);
        }
    }
}
