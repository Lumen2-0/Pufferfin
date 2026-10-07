using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Ebooks;

namespace Pufferfin.Server.Api;

/// <summary>
/// The ebook setup, for the administration dashboard.
/// </summary>
[Route("Pufferfin/Ebooks")]
[Tags("Pufferfin")]
[Authorize(Policy = Policies.RequiresElevation)]
public class PufferfinEbooksController : BaseJellyfinApiController
{
    private readonly IPufferfinConfigurationStore _store;
    private readonly EbookHealthCheck _check;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinEbooksController"/> class.
    /// </summary>
    /// <param name="store">The configuration store.</param>
    /// <param name="check">The ebook checks.</param>
    public PufferfinEbooksController(IPufferfinConfigurationStore store, EbookHealthCheck check)
    {
        _store = store;
        _check = check;
    }

    /// <summary>
    /// Checks the ebook setup: the Shelfmark key, the books category of qBittorrent, the library folder, the Jellyfin
    /// library of type Books that indexes it, and whether hard links are possible.
    /// When Shelfmark is not configured, books are off and nothing is checked: not an error.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The checks.</response>
    /// <returns>The report.</returns>
    [HttpGet("Health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<EbookHealthReport>> GetPufferfinEbookHealth(CancellationToken cancellationToken)
        => await _check.RunAsync(_store.Current.Settings, cancellationToken).ConfigureAwait(false);
}
