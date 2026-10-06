using Jellyfin.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Api;

/// <summary>
/// Pufferfin capabilities, for every signed in user: the client uses them to show or hide features.
/// </summary>
[Route("Pufferfin/Capabilities")]
[Tags("Pufferfin")]
[Authorize]
public class PufferfinCapabilitiesController : BaseJellyfinApiController
{
    private readonly IPufferfinConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinCapabilitiesController"/> class.
    /// </summary>
    /// <param name="store">The configuration store.</param>
    public PufferfinCapabilitiesController(IPufferfinConfigurationStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Gets which services are configured and enabled.
    /// </summary>
    /// <response code="200">Capabilities returned.</response>
    /// <returns>The capabilities.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PufferfinCapabilitiesDto> GetPufferfinCapabilities()
        => PufferfinCapabilitiesDto.From(_store.Current.Settings);
}
