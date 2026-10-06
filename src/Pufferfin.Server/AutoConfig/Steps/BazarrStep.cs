using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.AutoConfig.Steps;

/// <summary>
/// Connects Bazarr to Radarr and Sonarr. A connection Bazarr already uses is left as it is.
/// </summary>
public sealed class BazarrStep : IAutoConfigStep
{
    private readonly BazarrClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="BazarrStep"/> class.
    /// </summary>
    /// <param name="client">The Bazarr client.</param>
    public BazarrStep(BazarrClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public string Name => "Bazarr connections";

    /// <inheritdoc />
    public async Task<AutoConfigStepOutcome> ApplyAsync(PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GetService(PufferfinService.Bazarr) is not { } bazarr)
        {
            return StepOutcomes.NotConfigured("Bazarr");
        }

        var created = new List<string>();
        try
        {
            var current = await _client.GetSettingsAsync(bazarr, cancellationToken).ConfigureAwait(false);
            var fields = new List<KeyValuePair<string, string>>();
            foreach (var service in new[] { PufferfinService.Radarr, PufferfinService.Sonarr })
            {
                var section = service.ToString().ToLowerInvariant();
                if (settings.GetService(service) is not { BaseUrl: { } url } target
                    || current["general"]?["use_" + section]?.GetValue<bool>() == true)
                {
                    continue;
                }

                var prefix = "settings-" + section + "-";
                fields.Add(new("settings-general-use_" + section, "true"));
                fields.Add(new(prefix + "ip", url.Host));
                fields.Add(new(prefix + "port", url.Port.ToString(CultureInfo.InvariantCulture)));
                fields.Add(new(prefix + "base_url", url.AbsolutePath.TrimEnd('/') is { Length: > 0 } path ? path : "/"));
                fields.Add(new(prefix + "ssl", url.Scheme == Uri.UriSchemeHttps ? "true" : "false"));
                fields.Add(new(prefix + "apikey", target.ApiKey ?? string.Empty));
                created.Add("the connection to " + service);
            }

            if (fields.Count > 0)
            {
                await _client.SaveSettingsAsync(bazarr, fields, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ArrApiException ex)
        {
            return StepOutcomes.Failed(ex);
        }

        return StepOutcomes.FromCreated(created);
    }
}
