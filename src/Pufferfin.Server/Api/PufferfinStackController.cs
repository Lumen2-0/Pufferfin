using System.Collections.Generic;
using Jellyfin.Api;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Stack;

namespace Pufferfin.Server.Api;

/// <summary>
/// The management pages of the *arr stack, for administrators.
/// </summary>
[Route("Pufferfin/Stack")]
[Tags("Pufferfin")]
[Authorize(Policy = Policies.RequiresElevation)]
public class PufferfinStackController : BaseJellyfinApiController
{
    private readonly StackLinks _links;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinStackController"/> class.
    /// </summary>
    /// <param name="links">The stack links.</param>
    public PufferfinStackController(StackLinks links)
    {
        _links = links;
    }

    /// <summary>
    /// Gets the management pages configured by the environment; empty when none is.
    /// </summary>
    /// <response code="200">Links returned.</response>
    /// <returns>The links.</returns>
    [HttpGet("Links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<StackLink>> GetPufferfinStackLinks() => Ok(_links.Get());
}
