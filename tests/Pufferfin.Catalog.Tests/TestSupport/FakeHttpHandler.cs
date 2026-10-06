using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pufferfin.Catalog.Tests.TestSupport;

/// <summary>
/// Answers HTTP requests from a delegate and records them.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public ConcurrentQueue<string> AuthorizationHeaders { get; } = new();

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status)
        => new(status) { Content = new StringContent(string.Empty) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request.RequestUri!);
        if (request.Headers.Authorization is not null)
        {
            AuthorizationHeaders.Enqueue(request.Headers.Authorization.ToString());
        }

        return _respond(request, cancellationToken);
    }
}
