namespace Pufferfin.Server.Requests;

/// <summary>
/// Wakes the request processor for a round now.
/// </summary>
public interface IRequestWaker
{
    /// <summary>
    /// Starts a round now rather than at the next interval.
    /// </summary>
    void Wake();
}
