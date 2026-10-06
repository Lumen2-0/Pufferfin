using System;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Connectivity;

/// <summary>
/// The result of a connection test.
/// </summary>
/// <param name="Service">The tested service.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Detail">A short diagnostic written by the server. Never contains a secret, a URL or a remote message.</param>
/// <param name="Duration">How long the test took.</param>
public sealed record ConnectionTestResult(PufferfinService Service, ConnectionStatus Status, string? Detail, TimeSpan Duration);
