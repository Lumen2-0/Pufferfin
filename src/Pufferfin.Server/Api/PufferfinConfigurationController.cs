using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;

namespace Pufferfin.Server.Api;

/// <summary>
/// Pufferfin configuration administration. Administrators only, on every action.
/// </summary>
[Route("Pufferfin/Configuration")]
[Tags("Pufferfin")]
[Authorize(Policy = Policies.RequiresElevation)]
public class PufferfinConfigurationController : BaseJellyfinApiController
{
    private readonly IPufferfinConfigurationStore _store;
    private readonly IServiceConnectionTester _connectionTester;
    private readonly IAutoConfigRunner _autoConfigRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinConfigurationController"/> class.
    /// </summary>
    /// <param name="store">The configuration store.</param>
    /// <param name="connectionTester">The connection tester.</param>
    /// <param name="autoConfigRunner">The auto-configuration runner.</param>
    public PufferfinConfigurationController(IPufferfinConfigurationStore store, IServiceConnectionTester connectionTester, IAutoConfigRunner autoConfigRunner)
    {
        _store = store;
        _connectionTester = connectionTester;
        _autoConfigRunner = autoConfigRunner;
    }

    /// <summary>
    /// Gets the Pufferfin configuration. Secrets are reported as set or empty, never returned.
    /// </summary>
    /// <response code="200">Configuration returned.</response>
    /// <returns>The configuration.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PufferfinConfigurationDto> GetPufferfinConfiguration()
        => PufferfinConfigurationDto.From(_store.Current);

    /// <summary>
    /// Updates part of the Pufferfin configuration, then replays the auto-configuration in the background.
    /// </summary>
    /// <param name="update">The changes.</param>
    /// <response code="200">Configuration saved. The new configuration is returned.</response>
    /// <response code="400">The update is invalid. Nothing was saved.</response>
    /// <returns>The new configuration.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<PufferfinConfigurationDto> UpdatePufferfinConfiguration([FromBody, Required] UpdatePufferfinConfigurationDto update)
    {
        try
        {
            return PufferfinConfigurationDto.From(_store.Update(update.Settings));
        }
        catch (SettingsValidationException ex)
        {
            return BadRequest(new ValidationProblemDetails(ex.Errors.ToDictionary(e => e.Key, e => e.Value)));
        }
    }

    /// <summary>
    /// Tests the connection to a service with its saved settings. Bounded by a timeout.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Test completed. The outcome is in the body, including when the service is unreachable.</response>
    /// <returns>The test result.</returns>
    [HttpPost("Services/{service}/Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConnectionTestResultDto>> TestPufferfinServiceConnection([FromRoute, Required] PufferfinService service, CancellationToken cancellationToken)
    {
        var result = await _connectionTester.TestAsync(service, _store.Current.Settings, cancellationToken).ConfigureAwait(false);
        return ConnectionTestResultDto.From(result);
    }

    /// <summary>
    /// Gets the result of the last auto-configuration run.
    /// </summary>
    /// <response code="200">Last run returned.</response>
    /// <response code="204">No run has completed yet.</response>
    /// <returns>The last run.</returns>
    [HttpGet("AutoConfig")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult<AutoConfigRunDto> GetPufferfinAutoConfig()
        => _autoConfigRunner.LastRun is { } lastRun ? AutoConfigRunDto.From(lastRun) : NoContent();

    /// <summary>
    /// Replays the auto-configuration now and waits for the result.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Run completed.</response>
    /// <returns>The run result.</returns>
    [HttpPost("AutoConfig")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AutoConfigRunDto>> RunPufferfinAutoConfig(CancellationToken cancellationToken)
    {
        var result = await _autoConfigRunner.RunAsync(AutoConfigTrigger.Manual, cancellationToken).ConfigureAwait(false);
        return AutoConfigRunDto.From(result);
    }
}
