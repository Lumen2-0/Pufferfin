namespace Pufferfin.Server.Arr;

/// <summary>
/// Why a call to a service failed.
/// </summary>
public enum ArrFailure
{
    /// <summary>
    /// The service could not be reached.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The service did not answer in time.
    /// </summary>
    TimedOut,

    /// <summary>
    /// The service refused the API key or the credentials.
    /// </summary>
    KeyRejected,

    /// <summary>
    /// The resource does not exist.
    /// </summary>
    NotFound,

    /// <summary>
    /// The service refused the request, usually a validation error. The remote messages say why.
    /// </summary>
    Rejected,

    /// <summary>
    /// The service failed while handling the request.
    /// </summary>
    ServerError,

    /// <summary>
    /// The service answered with something that could not be read.
    /// </summary>
    InvalidResponse
}
