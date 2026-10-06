using Pufferfin.Server.Requests;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// Why a request failed or waits.
/// </summary>
public sealed class RequestProblemDto
{
    /// <summary>Gets the code, for the client to translate.</summary>
    public required RequestProblemCode Code { get; init; }

    /// <summary>Gets an English sentence written by the server.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the reason given by Radarr, Sonarr or qBittorrent, when there is one.</summary>
    public string? Detail { get; init; }
}
