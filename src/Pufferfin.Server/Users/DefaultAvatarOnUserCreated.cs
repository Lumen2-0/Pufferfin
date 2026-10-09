using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Users;

/// <summary>
/// Gives a new account its default picture as soon as it is created.
/// </summary>
public sealed class DefaultAvatarOnUserCreated : IEventConsumer<UserCreatedEventArgs>
{
    private readonly DefaultAvatars _avatars;
    private readonly ILogger<DefaultAvatarOnUserCreated> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAvatarOnUserCreated"/> class.
    /// </summary>
    /// <param name="avatars">The default pictures.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAvatarOnUserCreated(DefaultAvatars avatars, ILogger<DefaultAvatarOnUserCreated> logger)
    {
        _avatars = avatars;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnEvent(UserCreatedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        try
        {
            await _avatars.ApplyAsync(eventArgs.Argument, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The account exists: a missing picture must not fail its creation.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError("The default picture of a new account could not be given ({ErrorType})", ex.GetType().Name);
        }
    }
}
