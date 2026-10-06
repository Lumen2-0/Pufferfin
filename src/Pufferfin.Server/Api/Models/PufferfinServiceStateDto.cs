using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// Whether a service is enabled.
/// </summary>
public sealed class PufferfinServiceStateDto
{
    /// <summary>
    /// Gets the service.
    /// </summary>
    public required PufferfinService Service { get; init; }

    /// <summary>
    /// Gets a value indicating whether the service is configured, hence enabled.
    /// </summary>
    public required bool IsEnabled { get; init; }
}
