using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Deletion;

namespace Pufferfin.Server.Api;

/// <summary>
/// Deletes titles of the library, safely by default: private torrents keep seeding until their minimum.
/// Rights are checked here, on the server: administrators, and users an administrator allowed to delete.
/// Only administrators may delete immediately, after an explicit confirmation.
/// </summary>
[Route("Pufferfin/Library")]
[Tags("Pufferfin")]
[Authorize]
public class PufferfinLibraryController : BaseJellyfinApiController
{
    private readonly ContentDeletionService _deletion;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinLibraryController"/> class.
    /// </summary>
    /// <param name="deletion">The deletion service.</param>
    public PufferfinLibraryController(ContentDeletionService deletion)
    {
        _deletion = deletion;
    }

    private bool IsAdministrator => User.IsInRole(UserRoles.Administrator);

    /// <summary>
    /// Tells what deleting a title would do, without deleting anything: which torrents go now, which keep seeding and
    /// until when, and the warning of the immediate mode.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id of a film, a series, a season, an episode or an audiobook.</param>
    /// <param name="mode">The mode; safe when not given.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Plan returned.</response>
    /// <response code="403">The user may not delete, or may not delete immediately.</response>
    /// <response code="404">No such title.</response>
    /// <response code="409">Radarr or Sonarr, which holds the title, is not configured.</response>
    /// <response code="503">A service could not be reached.</response>
    /// <returns>The outcome, with the plan.</returns>
    [HttpGet("{itemId}/Deletion")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DeletionOutcome>> GetPufferfinDeletionPlan(
        [FromRoute, Required] Guid itemId,
        [FromQuery] DeletionMode mode = DeletionMode.Safe,
        CancellationToken cancellationToken = default)
        => ToResult(await _deletion.PreviewAsync(itemId, mode, User.GetUserId(), IsAdministrator, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Deletes a title. The safe mode is the default. The immediate mode needs <c>confirm=true</c>: without it,
    /// nothing is deleted and the answer is 409 with the plan and its warning.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id of a film, a series, a season, an episode or an audiobook.</param>
    /// <param name="mode">The mode; safe when not given.</param>
    /// <param name="confirm">Whether the user confirmed the immediate mode after reading its warning.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Title deleted.</response>
    /// <response code="403">The user may not delete, or may not delete immediately.</response>
    /// <response code="404">No such title.</response>
    /// <response code="409">The immediate mode was not confirmed, or Radarr or Sonarr is not configured. Nothing was deleted.</response>
    /// <response code="503">A service could not be reached, or the files could not be deleted. Nothing was deleted.</response>
    /// <returns>The outcome.</returns>
    [HttpDelete("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(DeletionOutcome), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DeletionOutcome>> DeletePufferfinLibraryItem(
        [FromRoute, Required] Guid itemId,
        [FromQuery] DeletionMode mode = DeletionMode.Safe,
        [FromQuery] bool confirm = false,
        CancellationToken cancellationToken = default)
        => ToResult(await _deletion.DeleteAsync(itemId, mode, confirm, User.GetUserId(), IsAdministrator, cancellationToken).ConfigureAwait(false));

    private ActionResult<DeletionOutcome> ToResult(DeletionOutcome outcome) => outcome.Status switch
    {
        DeletionStatus.Planned or DeletionStatus.Deleted => outcome,
        DeletionStatus.ConfirmationRequired => Conflict(outcome),
        DeletionStatus.Forbidden => Problem(detail: outcome.Message, statusCode: StatusCodes.Status403Forbidden, title: outcome.Status.ToString()),
        DeletionStatus.NotFound => Problem(detail: outcome.Message, statusCode: StatusCodes.Status404NotFound, title: outcome.Status.ToString()),
        DeletionStatus.ServiceDisabled => Problem(detail: outcome.Message, statusCode: StatusCodes.Status409Conflict, title: outcome.Status.ToString()),
        _ => Problem(detail: outcome.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: outcome.Status.ToString())
    };
}
