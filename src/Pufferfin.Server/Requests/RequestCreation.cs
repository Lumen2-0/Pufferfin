namespace Pufferfin.Server.Requests;

/// <summary>
/// The outcome of a request creation.
/// </summary>
/// <param name="Request">The request, created or already on its way.</param>
/// <param name="Existing">Whether the user already had this request on its way.</param>
/// <param name="Refusal">Why the request was refused, if it was.</param>
/// <param name="Message">An English sentence explaining the refusal.</param>
public sealed record RequestCreation(MediaRequest? Request, bool Existing, RequestRefusal? Refusal, string? Message)
{
    /// <summary>
    /// Builds a refusal.
    /// </summary>
    /// <param name="refusal">Why.</param>
    /// <param name="message">An English sentence.</param>
    /// <returns>The outcome.</returns>
    public static RequestCreation Refused(RequestRefusal refusal, string message) => new(null, false, refusal, message);
}
