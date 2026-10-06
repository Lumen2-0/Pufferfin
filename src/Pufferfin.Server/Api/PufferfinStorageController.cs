using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Api;

/// <summary>
/// The storage screen and the seeding policy, for administrators. Never returns a tracker URL nor a passkey.
/// </summary>
[Route("Pufferfin/Storage")]
[Tags("Pufferfin")]
[Authorize(Policy = Policies.RequiresElevation)]
public class PufferfinStorageController : BaseJellyfinApiController
{
    private readonly StorageService _storage;
    private readonly SeedPolicyRunner _runner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinStorageController"/> class.
    /// </summary>
    /// <param name="storage">The storage service.</param>
    /// <param name="runner">The seeding policy runner.</param>
    public PufferfinStorageController(StorageService storage, SeedPolicyRunner runner)
    {
        _storage = storage;
        _runner = runner;
    }

    /// <summary>
    /// Gets the real disk usage, the hard link advice, and the seeding state of each torrent.
    /// </summary>
    /// <param name="refreshHardlinks">Whether to check the hard links again rather than use the result of the last hour.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Report returned.</response>
    /// <returns>The report.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<StorageReport>> GetPufferfinStorage([FromQuery] bool refreshHardlinks = false, CancellationToken cancellationToken = default)
        => await _storage.GetReportAsync(refreshHardlinks, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs the seeding policy now rather than at the next hourly cycle. Safe at any time: a cycle is idempotent,
    /// and no torrent is purged before its minimum seeding time.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Cycle run; the result says what was purged.</response>
    /// <returns>The result.</returns>
    [HttpPost("SeedPolicy/Run")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SeedCycleResult>> RunPufferfinSeedPolicy(CancellationToken cancellationToken)
        => await _runner.RunCycleAsync(cancellationToken).ConfigureAwait(false);
}
