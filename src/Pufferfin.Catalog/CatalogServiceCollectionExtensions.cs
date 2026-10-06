using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Caching;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Letterboxd;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Recommendations;
using Pufferfin.Catalog.Search;
using Pufferfin.Catalog.Tmdb;

namespace Pufferfin.Catalog;

/// <summary>
/// Registers the Pufferfin catalog.
/// </summary>
public static class CatalogServiceCollectionExtensions
{
    /// <summary>
    /// The timeout of one external catalog request.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Adds the catalog services and their HTTP clients.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddPufferfinCatalog(this IServiceCollection services)
    {
        // Interim source of the settings: the persisted configuration store registers its own provider and wins.
        services.TryAddSingleton<ICatalogSettingsProvider, EnvironmentCatalogSettingsProvider>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddSingleton<ICatalogCache, CatalogCache>();
        services.AddSingleton<CatalogRateLimiters>();
        services.AddSingleton<SearchRateLimiter>();
        services.AddSingleton<ILocalCatalogIndex, JellyfinLocalCatalogIndex>();
        services.AddSingleton<LocalAvailability>();
        services.AddSingleton<TmdbClient>();
        services.AddSingleton<TmdbCatalogService>();
        services.AddSingleton<AudibleCatalogService>();
        services.AddSingleton<LetterboxdClient>();
        services.AddSingleton<RecommendationService>();
        services.AddSingleton<UniversalSearchService>();

        AddCatalogHttpClient(services, TmdbClient.HttpClientName, "application/json");
        AddCatalogHttpClient(services, AudibleCatalogService.HttpClientName, "application/json");
        AddCatalogHttpClient(services, LetterboxdClient.HttpClientName, "application/rss+xml");
        return services;
    }

    private static void AddCatalogHttpClient(IServiceCollection services, string name, string accept)
    {
        services.AddHttpClient(name, client =>
            {
                client.Timeout = RequestTimeout;
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pufferfin", "1.0"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })

            // The default handler logs every request URI, and a TMDB v3 key travels in the query string.
            .RemoveAllLoggers();
    }
}
