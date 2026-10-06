using System.Threading;
using System.Threading.Tasks;

namespace Pufferfin.Server.Requests.Workflow;

/// <summary>
/// Drives the requests of some kinds through Radarr or Sonarr. Each call returns the next version of the request,
/// or the same instance when nothing changed; the processor stores it.
/// </summary>
public interface IRequestWorkflow
{
    /// <summary>
    /// Gets a value indicating whether the workflow handles a kind of request.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns><c>true</c> when it does.</returns>
    bool Handles(RequestKind kind);

    /// <summary>
    /// Adds the title to the service if needed, checks the queue and the history against a double download,
    /// searches, picks a release by language and grabs it.
    /// </summary>
    /// <param name="request">A request in <see cref="RequestState.Searching"/>.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next version of the request.</returns>
    Task<MediaRequest> SearchAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Follows a request that waits for sources, downloads or imports: progress, import, availability in the library, failures.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next version of the request, or the same instance.</returns>
    Task<MediaRequest> TrackAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken);
}
