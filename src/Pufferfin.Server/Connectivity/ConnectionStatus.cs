namespace Pufferfin.Server.Connectivity;

/// <summary>
/// The outcome of a connection test.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>
    /// The service is not configured, hence disabled. Not an error.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// The service answered and accepted the credentials.
    /// </summary>
    Connected,

    /// <summary>
    /// The service could not be reached in time, or did not answer like the expected service.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The service answered but refused the API key or the credentials.
    /// </summary>
    KeyRejected
}
