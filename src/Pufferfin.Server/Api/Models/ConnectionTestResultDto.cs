using System;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// The result of a connection test.
/// </summary>
public sealed class ConnectionTestResultDto
{
    /// <summary>
    /// Gets the tested service.
    /// </summary>
    public required PufferfinService Service { get; init; }

    /// <summary>
    /// Gets the outcome.
    /// </summary>
    public required ConnectionStatus Status { get; init; }

    /// <summary>
    /// Gets a short diagnostic, in English, when the test did not succeed.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Gets how long the test took, in milliseconds.
    /// </summary>
    public required long DurationMs { get; init; }

    /// <summary>
    /// Creates the DTO of a result.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The DTO.</returns>
    public static ConnectionTestResultDto From(ConnectionTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ConnectionTestResultDto
        {
            Service = result.Service,
            Status = result.Status,
            Detail = result.Detail,
            DurationMs = (long)result.Duration.TotalMilliseconds
        };
    }
}
