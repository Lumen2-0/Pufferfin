namespace Pufferfin.Server.Requests;

/// <summary>
/// Why a request failed or waits, in plain words.
/// </summary>
/// <param name="Code">The code, for the client to translate.</param>
/// <param name="Message">An English sentence written by Pufferfin.</param>
/// <param name="Detail">The reason given by Radarr, Sonarr or qBittorrent, when there is one. Never a secret.</param>
public sealed record RequestProblem(RequestProblemCode Code, string Message, string? Detail = null);
