using System;

namespace Pufferfin.Server.Requests;

/// <summary>
/// Moves a request from one state to the next.
/// </summary>
public static class RequestTransitions
{
    /// <summary>
    /// Moves a request to a state, with a problem or none. The live progress is dropped when the request stops downloading.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="state">The new state.</param>
    /// <param name="now">The current time.</param>
    /// <param name="problem">Why it fails or waits, if it does.</param>
    /// <returns>The new version, or the same instance when neither the state nor the problem changes.</returns>
    public static MediaRequest To(this MediaRequest request, RequestState state, DateTimeOffset now, RequestProblem? problem = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (state == RequestState.Failed && problem is null)
        {
            throw new ArgumentException("A failed request always says why.", nameof(problem));
        }

        if (request.State == state && request.Problem == problem)
        {
            return request;
        }

        return request with
        {
            // A temporary film counts its days from its import, and only when it is its own download.
            ExpiresAt = request.ExpiresAt ?? (state == RequestState.Available ? TemporaryDownloads.ExpiryFor(request, now) : null),
            State = state,
            Problem = problem,
            UpdatedAt = now,
            Progress = state == RequestState.Downloading ? request.Progress : null,
            NextAttemptAt = state is RequestState.WaitingForSources or RequestState.Searching ? request.NextAttemptAt : null
        };
    }

    /// <summary>
    /// Fails a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="now">The current time.</param>
    /// <param name="code">The problem code.</param>
    /// <param name="message">An English sentence.</param>
    /// <param name="detail">The reason given by a service, if any.</param>
    /// <returns>The failed request.</returns>
    public static MediaRequest Fail(this MediaRequest request, DateTimeOffset now, RequestProblemCode code, string message, string? detail = null)
        => request.To(RequestState.Failed, now, new RequestProblem(code, message, detail));

    /// <summary>
    /// Puts a request in wait for sources, to be searched again later. Never a failure.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="now">The current time.</param>
    /// <param name="problem">Why it waits.</param>
    /// <returns>The waiting request.</returns>
    public static MediaRequest Wait(this MediaRequest request, DateTimeOffset now, RequestProblem problem)
    {
        var waiting = request.To(RequestState.WaitingForSources, now, problem);
        return waiting with { NextAttemptAt = now + RetryDelay(request.Attempts) };
    }

    /// <summary>
    /// Gets the delay before a request waiting for sources is searched again: 30 minutes, doubling, at most 6 hours.
    /// The services keep watching their RSS feeds in between.
    /// </summary>
    /// <param name="attempts">The searches run so far.</param>
    /// <returns>The delay.</returns>
    public static TimeSpan RetryDelay(int attempts)
        => TimeSpan.FromMinutes(Math.Min(360, 30 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 8))));
}
