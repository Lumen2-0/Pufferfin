using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// The Pufferfin configuration, as exposed to administrators. Contains no secret.
/// </summary>
public sealed class PufferfinConfigurationDto
{
    /// <summary>
    /// Gets every setting, in display order.
    /// </summary>
    public required IReadOnlyList<PufferfinSettingDto> Settings { get; init; }

    /// <summary>
    /// Gets the state of every service.
    /// </summary>
    public required IReadOnlyList<PufferfinServiceStateDto> Services { get; init; }

    /// <summary>
    /// Creates the DTO of a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The DTO.</returns>
    public static PufferfinConfigurationDto From(PufferfinConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PufferfinConfigurationDto
        {
            Settings = snapshot.States.Select(PufferfinSettingDto.From).ToList(),
            Services = Enum.GetValues<PufferfinService>()
                .Select(s => new PufferfinServiceStateDto { Service = s, IsEnabled = snapshot.Settings.IsEnabled(s) })
                .ToList()
        };
    }
}
