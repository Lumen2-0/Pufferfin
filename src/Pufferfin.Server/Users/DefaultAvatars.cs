using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Users;

/// <summary>
/// Gives an account without a picture its default one (see <see cref="DefaultAvatar"/>). A picture the user chose is
/// never replaced.
/// </summary>
public sealed class DefaultAvatars
{
    private const string FileName = "profile.png";

    private readonly IUserManager _userManager;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly ILogger<DefaultAvatars> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAvatars"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="configurationManager">The server configuration manager, for the folder of the accounts.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAvatars(IUserManager userManager, IServerConfigurationManager configurationManager, ILogger<DefaultAvatars> logger)
    {
        _userManager = userManager;
        _configurationManager = configurationManager;
        _logger = logger;
    }

    /// <summary>
    /// Gives every account without a picture its default one.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of accounts given a picture.</returns>
    public async Task<int> ApplyToAllAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var user in _userManager.GetUsers().Where(u => u.ProfileImage is null).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ApplyAsync(user, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Gives an account its default picture, when it has none.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the account was given a picture.</returns>
    public async Task<bool> ApplyAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.ProfileImage is not null)
        {
            return false;
        }

        // The folder the server keeps the pictures of the accounts in, as an uploaded picture
        var root = Path.GetFullPath(_configurationManager.ApplicationPaths.UserConfigurationDirectoryPath);
        var folder = Path.GetFullPath(Path.Combine(root, user.Username));
        if (!folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            _logger.LogWarning("No default picture for an account whose name is not a folder name");
            return false;
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        await File.WriteAllBytesAsync(path, DefaultAvatar.Render(user.Id), cancellationToken).ConfigureAwait(false);
        user.ProfileImage = new ImageInfo(path);
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
        return true;
    }
}
