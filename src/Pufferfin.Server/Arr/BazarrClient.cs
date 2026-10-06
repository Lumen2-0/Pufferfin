using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Arr;

/// <summary>
/// The settings API of Bazarr.
/// </summary>
public sealed class BazarrClient
{
    private readonly ArrHttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="BazarrClient"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    public BazarrClient(ArrHttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Gets the settings, by section (<c>general</c>, <c>radarr</c>, <c>sonarr</c>...).
    /// </summary>
    /// <param name="service">The Bazarr settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The settings.</returns>
    public Task<JsonObject> GetSettingsAsync(ServiceSettings service, CancellationToken cancellationToken)
        => _http.GetAsync<JsonObject>(service, "api/system/settings", cancellationToken);

    /// <summary>
    /// Changes some settings; each field is named <c>settings-{section}-{key}</c>.
    /// </summary>
    /// <param name="service">The Bazarr settings.</param>
    /// <param name="fields">The fields to change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public Task SaveSettingsAsync(ServiceSettings service, IReadOnlyList<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
        => _http.PostFormAsync(service, "api/system/settings", fields, cancellationToken);
}
