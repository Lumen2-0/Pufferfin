using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Extensions;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Api;

/// <summary>
/// Download requests: films through Radarr, seasons and episodes through Sonarr.
/// Rights, quotas and rate limits are checked here, on the server. A user only sees their own requests;
/// administrators see them all, approve them and order the queue.
/// </summary>
[Route("Pufferfin/Requests")]
[Tags("Pufferfin")]
[Authorize]
public class PufferfinRequestsController : BaseJellyfinApiController
{
    private readonly RequestService _service;
    private readonly RequestStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinRequestsController"/> class.
    /// </summary>
    /// <param name="service">The request service.</param>
    /// <param name="store">The request store.</param>
    public PufferfinRequestsController(RequestService service, RequestStore store)
    {
        _service = service;
        _store = store;
    }

    private bool IsAdministrator => User.IsInRole(UserRoles.Administrator);

    /// <summary>
    /// Requests a film, a season or an episode.
    /// </summary>
    /// <param name="request">What the user asks for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="201">Request created.</response>
    /// <response code="200">The user already has this request on its way; it is returned.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="403">The account may not request titles.</response>
    /// <response code="404">TMDB does not know the title.</response>
    /// <response code="409">Radarr or Sonarr is not configured.</response>
    /// <response code="429">The weekly quota is reached, or too many requests in a minute.</response>
    /// <returns>The request.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MediaRequestDto>> CreatePufferfinRequest([FromBody, Required] CreateMediaRequestDto request, CancellationToken cancellationToken)
    {
        var input = new RequestInput(request.Kind, request.TmdbId, request.SeasonNumber, request.EpisodeNumber, request.AirDate, request.Language?.Trim());
        var outcome = await _service.CreateAsync(User.GetUserId(), IsAdministrator, input, cancellationToken).ConfigureAwait(false);
        if (outcome.Request is { } created)
        {
            var dto = ToDto(created);
            return outcome.Existing ? Ok(dto) : StatusCode(StatusCodes.Status201Created, dto);
        }

        var status = outcome.Refusal switch
        {
            RequestRefusal.ServiceDisabled => StatusCodes.Status409Conflict,
            RequestRefusal.NotAllowed => StatusCodes.Status403Forbidden,
            RequestRefusal.QuotaExceeded or RequestRefusal.RateLimited => StatusCodes.Status429TooManyRequests,
            RequestRefusal.TitleUnknown => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
        return Problem(detail: outcome.Message, statusCode: status, title: outcome.Refusal.ToString());
    }

    /// <summary>
    /// Lists requests, in queue order: the user's own, or every request for an administrator who asks for all.
    /// </summary>
    /// <param name="all">Whether an administrator wants every request.</param>
    /// <param name="activeOnly">Whether to leave out available, cancelled and refused requests.</param>
    /// <response code="200">Requests returned.</response>
    /// <returns>The requests.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MediaRequestDto[]> GetPufferfinRequests([FromQuery] bool all = false, [FromQuery] bool activeOnly = false)
    {
        var userId = User.GetUserId();
        return _store.GetAll()
            .Where(r => (all && IsAdministrator) || r.UserId.Equals(userId))
            .Where(r => !activeOnly || r.IsActive || r.State == RequestState.Failed)
            .Select(ToDto)
            .ToArray();
    }

    /// <summary>
    /// Gets a request.
    /// </summary>
    /// <param name="requestId">The request id.</param>
    /// <response code="200">Request returned.</response>
    /// <response code="404">No such request for this user.</response>
    /// <returns>The request.</returns>
    [HttpGet("{requestId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<MediaRequestDto> GetPufferfinRequest([FromRoute, Required] Guid requestId)
    {
        var request = _store.Get(requestId);
        return request is not null && (IsAdministrator || request.UserId.Equals(User.GetUserId())) ? ToDto(request) : NotFound();
    }

    /// <summary>
    /// Cancels a request that is not available yet. Nothing is deleted from Radarr, Sonarr or qBittorrent.
    /// </summary>
    /// <param name="requestId">The request id.</param>
    /// <response code="200">Request cancelled.</response>
    /// <response code="404">No such request for this user.</response>
    /// <response code="409">The request is already available, cancelled or refused.</response>
    /// <returns>The request.</returns>
    [HttpPost("{requestId}/Cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<MediaRequestDto> CancelPufferfinRequest([FromRoute, Required] Guid requestId)
        => ToResult(_service.Cancel(requestId, User.GetUserId(), IsAdministrator));

    /// <summary>
    /// Puts a failed request back in the queue.
    /// </summary>
    /// <param name="requestId">The request id.</param>
    /// <response code="200">Request queued again.</response>
    /// <response code="404">No such request for this user.</response>
    /// <response code="409">The request has not failed.</response>
    /// <returns>The request.</returns>
    [HttpPost("{requestId}/Retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<MediaRequestDto> RetryPufferfinRequest([FromRoute, Required] Guid requestId)
        => ToResult(_service.Retry(requestId, User.GetUserId(), IsAdministrator));

    /// <summary>
    /// Approves a request waiting for approval.
    /// </summary>
    /// <param name="requestId">The request id.</param>
    /// <response code="200">Request approved and queued.</response>
    /// <response code="404">No such request.</response>
    /// <response code="409">The request is not waiting for approval.</response>
    /// <returns>The request.</returns>
    [HttpPost("{requestId}/Approve")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<MediaRequestDto> ApprovePufferfinRequest([FromRoute, Required] Guid requestId)
        => ToResult(_service.Approve(requestId, User.GetUserId()));

    /// <summary>
    /// Refuses a request waiting for approval.
    /// </summary>
    /// <param name="requestId">The request id.</param>
    /// <response code="200">Request refused.</response>
    /// <response code="404">No such request.</response>
    /// <response code="409">The request is not waiting for approval.</response>
    /// <returns>The request.</returns>
    [HttpPost("{requestId}/Reject")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<MediaRequestDto> RejectPufferfinRequest([FromRoute, Required] Guid requestId)
        => ToResult(_service.Reject(requestId, User.GetUserId()));

    /// <summary>
    /// Moves a request waiting for approval or for a slot to a new place in the queue.
    /// </summary>
    /// <param name="requestId">The request id.</param>
    /// <param name="move">The new place.</param>
    /// <response code="204">Request moved.</response>
    /// <response code="404">No such request waiting in the queue.</response>
    /// <returns>No content.</returns>
    [HttpPost("{requestId}/Position")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult MovePufferfinRequest([FromRoute, Required] Guid requestId, [FromBody, Required] MoveMediaRequestDto move)
        => _store.Move(requestId, move.Index) ? NoContent() : NotFound();

    /// <summary>
    /// Gets what the current user may request, and how much of the weekly quota is used.
    /// </summary>
    /// <response code="200">Permissions returned.</response>
    /// <returns>The permissions.</returns>
    [HttpGet("Permissions/Me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<RequestPermissionsDto> GetMyPufferfinRequestPermissions()
        => Permissions(User.GetUserId(), IsAdministrator);

    /// <summary>
    /// Gets what a user may request.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <response code="200">Permissions returned.</response>
    /// <returns>The permissions.</returns>
    [HttpGet("Permissions/{userId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<RequestPermissionsDto> GetPufferfinRequestPermissions([FromRoute, Required] Guid userId)
        => Permissions(userId, false);

    /// <summary>
    /// Sets what a user may request. Administrators may always request, without approval nor quota.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="permissions">The new permissions.</param>
    /// <response code="200">Permissions saved.</response>
    /// <response code="400">The quota is negative.</response>
    /// <returns>The saved permissions.</returns>
    [HttpPost("Permissions/{userId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<RequestPermissionsDto> SetPufferfinRequestPermissions([FromRoute, Required] Guid userId, [FromBody, Required] RequestPermissionsDto permissions)
    {
        if (permissions.WeeklyQuota is < 0)
        {
            return Problem(detail: "The weekly quota cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        _store.SetPermissions(userId, new RequestPermissions
        {
            CanRequest = permissions.CanRequest,
            RequiresApproval = permissions.RequiresApproval,
            WeeklyQuota = permissions.WeeklyQuota,
            CanDelete = permissions.CanDelete
        });
        return Permissions(userId, false);
    }

    /// <summary>
    /// Gets the audio language the current user chose for a series, used when a new request of the series names none.
    /// </summary>
    /// <param name="tmdbId">The TMDB id of the series.</param>
    /// <response code="200">Language returned: an ISO 639-1 code, <c>original</c>, or <c>null</c>.</response>
    /// <returns>The language.</returns>
    [HttpGet("SeriesLanguage/{tmdbId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<string?> GetPufferfinSeriesLanguage([FromRoute, Required] int tmdbId)
        => _store.GetSeriesLanguage(User.GetUserId(), tmdbId);

    private ActionResult<RequestPermissionsDto> Permissions(Guid userId, bool isAdministrator)
    {
        var weekAgo = DateTimeOffset.UtcNow - TimeSpan.FromDays(7);
        var used = _store.GetAll().Count(r => r.UserId.Equals(userId) && r.CreatedAt > weekAgo && r.State != RequestState.Rejected);
        return RequestPermissionsDto.From(_store.GetPermissions(userId), used, isAdministrator);
    }

    private ActionResult<MediaRequestDto> ToResult(RequestChange change) => change.Outcome switch
    {
        RequestChangeOutcome.Done => ToDto(change.Request!),
        RequestChangeOutcome.NotFound => NotFound(),
        RequestChangeOutcome.Forbidden => Forbid(),
        _ => Problem(detail: $"The request is {change.Request?.State}: this change does not apply.", statusCode: StatusCodes.Status409Conflict)
    };

    private MediaRequestDto ToDto(MediaRequest request)
    {
        var queued = _store.GetAll().Where(r => r.State == RequestState.Queued).Select(r => r.Id).ToList();
        var index = queued.FindIndex(id => id.Equals(request.Id));
        return MediaRequestDto.From(request, index < 0 ? null : index + 1);
    }
}
