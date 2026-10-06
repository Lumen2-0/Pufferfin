using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Arr;

namespace Pufferfin.Server.Tests.TestHelpers;

/// <summary>
/// An in-memory Radarr, Sonarr, Prowlarr, Bazarr or qBittorrent: resources are JSON arrays, POST appends with a new id,
/// PUT replaces by id. Routes can be overridden to simulate a specific answer.
/// </summary>
public sealed class FakeArrServer : HttpMessageHandler, IHttpClientFactory
{
    private int _nextId = 100;

    public Dictionary<string, JsonArray> Resources { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, JsonNode> Schemas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, JsonNode> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body, HttpRequestMessage Request)> Calls { get; } = new();

    public List<Func<HttpMethod, string, string?, HttpResponseMessage?>> Overrides { get; } = [];

    public IEnumerable<(HttpMethod Method, string Path, string? Body)> Writes
        => Calls.Where(c => c.Method != HttpMethod.Get).Select(c => (c.Method, c.Path, c.Body));

    public static ArrHttpClient CreateHttp(FakeArrServer server)
        => new(server, NullLogger<ArrHttpClient>.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.Zero);

    public static HttpResponseMessage Json(JsonNode? node, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(node?.ToJsonString() ?? "null", Encoding.UTF8, "application/json") };

    public JsonArray Resource(string name)
    {
        if (!Resources.TryGetValue(name, out var array))
        {
            array = [];
            Resources[name] = array;
        }

        return array;
    }

    public JsonObject Add(string resource, JsonObject item)
    {
        item["id"] ??= Interlocked.Increment(ref _nextId);
        Resource(resource).Add(item);
        return item;
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        foreach (var prefix in new[] { "/api/v3/", "/api/v2/", "/api/v1/", "/api/" })
        {
            var index = path.IndexOf(prefix, StringComparison.Ordinal);
            if (index >= 0)
            {
                path = path[(index + prefix.Length)..];
                break;
            }
        }

        var pathAndQuery = path + uri.Query;
        Calls.Enqueue((request.Method, pathAndQuery, body, request));
        foreach (var handle in Overrides)
        {
            if (handle(request.Method, pathAndQuery, body) is { } answer)
            {
                return answer;
            }
        }

        if (request.Method == HttpMethod.Get)
        {
            if (path.EndsWith("/schema", StringComparison.Ordinal) && Schemas.TryGetValue(path[..^"/schema".Length], out var schema))
            {
                return Json(schema.DeepClone());
            }

            if (Objects.TryGetValue(path, out var single))
            {
                return Json(single.DeepClone());
            }

            if (Resources.TryGetValue(path, out var items))
            {
                return Json(items.DeepClone());
            }

            var slash = path.LastIndexOf('/');
            if (slash > 0 && int.TryParse(path[(slash + 1)..], out var getId) && Resources.TryGetValue(path[..slash], out var owner))
            {
                var found = owner.OfType<JsonObject>().FirstOrDefault(i => i["id"]?.GetValue<int>() == getId);
                return found is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(found.DeepClone());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && body is not null && body.StartsWith('{'))
        {
            var item = JsonNode.Parse(body)!.AsObject();
            return Json(Add(path, item).DeepClone(), HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Put && body is not null)
        {
            var slash = path.LastIndexOf('/');
            if (slash > 0 && int.TryParse(path[(slash + 1)..], out var id) && Resources.TryGetValue(path[..slash], out var items))
            {
                var index = items.Select((item, i) => (item, i)).First(p => p.item?["id"]?.GetValue<int>() == id).i;
                items[index] = JsonNode.Parse(body);
                return Json(JsonNode.Parse(body), HttpStatusCode.Accepted);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}
