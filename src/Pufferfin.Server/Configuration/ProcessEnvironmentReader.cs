using System;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// Reads the environment variables of the current process.
/// </summary>
public sealed class ProcessEnvironmentReader : IEnvironmentReader
{
    /// <inheritdoc />
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}
