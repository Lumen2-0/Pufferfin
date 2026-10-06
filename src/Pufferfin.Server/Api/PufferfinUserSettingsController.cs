using System.ComponentModel.DataAnnotations;
using System.Linq;
using Jellyfin.Api;
using Jellyfin.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Users;

namespace Pufferfin.Server.Api;

/// <summary>
/// The Pufferfin settings of the current user. A user only ever reads and writes their own settings.
/// </summary>
[Route("Pufferfin/UserSettings")]
[Tags("Pufferfin")]
[Authorize]
public class PufferfinUserSettingsController : BaseJellyfinApiController
{
    private readonly IPufferfinUserSettingsStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinUserSettingsController"/> class.
    /// </summary>
    /// <param name="store">The user settings store.</param>
    public PufferfinUserSettingsController(IPufferfinUserSettingsStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Gets the Pufferfin settings of the current user.
    /// </summary>
    /// <response code="200">Settings returned.</response>
    /// <returns>The settings.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PufferfinUserSettingsDto> GetPufferfinUserSettings()
        => PufferfinUserSettingsDto.From(_store.Get(User.GetUserId()));

    /// <summary>
    /// Replaces the Pufferfin settings of the current user.
    /// </summary>
    /// <param name="settings">The new settings.</param>
    /// <response code="200">Settings saved. The saved settings are returned.</response>
    /// <response code="400">A value is invalid. Nothing was saved.</response>
    /// <returns>The saved settings.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<PufferfinUserSettingsDto> UpdatePufferfinUserSettings([FromBody, Required] PufferfinUserSettingsDto settings)
    {
        try
        {
            var userId = User.GetUserId();
            var saved = _store.Set(userId, new PufferfinUserSettings
            {
                LetterboxdUsername = settings.LetterboxdUsername,
                PreferredAudioLanguages = settings.PreferredAudioLanguages ?? _store.Get(userId).PreferredAudioLanguages
            });
            return PufferfinUserSettingsDto.From(saved);
        }
        catch (SettingsValidationException ex)
        {
            return BadRequest(new ValidationProblemDetails(ex.Errors.ToDictionary(e => e.Key, e => e.Value)));
        }
    }
}
