namespace Pufferfin.Server.Configuration;

/// <summary>
/// Reads environment variables. Abstracted so the precedence rules can be tested.
/// </summary>
public interface IEnvironmentReader
{
    /// <summary>
    /// Gets the value of an environment variable.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or <c>null</c> when the variable is not set.</returns>
    string? Get(string name);
}
