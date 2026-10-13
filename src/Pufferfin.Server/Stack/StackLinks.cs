using System;
using System.Collections.Generic;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Stack;

/// <summary>
/// The management pages of the *arr stack, read from the environment and never written in the code:
/// <c>PUFFERFIN_STACK_DOMAIN</c> gives each standard service at <c>https://{service}.{domain}</c>, and
/// <c>PUFFERFIN_STACK_LINKS</c> (<c>Name=https://url;Name2=https://url2</c>) replaces a service of the same name or adds one.
/// </summary>
public sealed class StackLinks
{
    /// <summary>
    /// The variable of the domain under which each standard service has its own sub-domain.
    /// </summary>
    public const string DomainVariable = "PUFFERFIN_STACK_DOMAIN";

    /// <summary>
    /// The variable of the explicit list of links.
    /// </summary>
    public const string LinksVariable = "PUFFERFIN_STACK_LINKS";

    private static readonly (string Name, string SubDomain)[] _standardServices =
    [
        ("Radarr", "radarr"),
        ("Sonarr", "sonarr"),
        ("Prowlarr", "prowlarr"),
        ("Bazarr", "bazarr"),
        ("qBittorrent", "qbittorrent"),
        ("Shelfmark", "shelfmark")
    ];

    private readonly IEnvironmentReader _environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="StackLinks"/> class.
    /// </summary>
    /// <param name="environment">The environment reader.</param>
    public StackLinks(IEnvironmentReader environment)
    {
        _environment = environment;
    }

    /// <summary>
    /// Gets the links of the current environment; empty when neither variable is set.
    /// </summary>
    /// <returns>The links, in the order of the standard services, then the added ones.</returns>
    public IReadOnlyList<StackLink> Get() => Build(_environment.Get(DomainVariable), _environment.Get(LinksVariable));

    /// <summary>
    /// Builds the links from the two variables. A malformed domain or entry is ignored, never thrown.
    /// </summary>
    /// <param name="domain">The value of <see cref="DomainVariable"/>.</param>
    /// <param name="links">The value of <see cref="LinksVariable"/>.</param>
    /// <returns>The links.</returns>
    public static IReadOnlyList<StackLink> Build(string? domain, string? links)
    {
        var result = new List<StackLink>();
        var host = NormalizeDomain(domain);
        if (host is not null)
        {
            foreach (var (name, subDomain) in _standardServices)
            {
                result.Add(new StackLink(name, $"https://{subDomain}.{host}"));
            }
        }

        foreach (var entry in (links ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = entry[..separator].Trim();
            var url = entry[(separator + 1)..].Trim();
            if (name.Length == 0 || !IsWebAddress(url))
            {
                continue;
            }

            var index = result.FindIndex(link => string.Equals(link.Name, name, StringComparison.OrdinalIgnoreCase));
            var link = new StackLink(name, url);
            if (index >= 0)
            {
                result[index] = link;
            }
            else
            {
                result.Add(link);
            }
        }

        return result;
    }

    private static string? NormalizeDomain(string? domain)
    {
        var host = domain?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        // A domain only: a scheme or a path given by mistake is dropped
        if (host.Contains("://", StringComparison.Ordinal))
        {
            host = host[(host.IndexOf("://", StringComparison.Ordinal) + 3)..];
        }

        host = host.Split('/')[0];
        return Uri.CheckHostName(host) == UriHostNameType.Dns ? host : null;
    }

    private static bool IsWebAddress(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
