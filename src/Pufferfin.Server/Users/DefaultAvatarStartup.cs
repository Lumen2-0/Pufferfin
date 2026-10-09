using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Users;

/// <summary>
/// At startup, gives the accounts that have no picture their default one.
/// </summary>
public sealed class DefaultAvatarStartup : BackgroundService
{
    private static readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(20);

    private readonly DefaultAvatars _avatars;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultAvatarStartup> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAvatarStartup"/> class.
    /// </summary>
    /// <param name="avatars">The default pictures.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAvatarStartup(DefaultAvatars avatars, TimeProvider timeProvider, ILogger<DefaultAvatarStartup> logger)
    {
        _avatars = avatars;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Once the accounts are loaded
            await Task.Delay(_startupDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
            var count = await _avatars.ApplyToAllAsync(stoppingToken).ConfigureAwait(false);
            if (count > 0)
            {
                _logger.LogInformation("Gave {Count} accounts their default picture", count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // A missing picture must not take the server down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError("The default pictures could not be given ({ErrorType})", ex.GetType().Name);
        }
    }
}
