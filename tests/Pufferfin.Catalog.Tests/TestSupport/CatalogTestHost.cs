using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Books;
using Pufferfin.Catalog.Caching;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Catalog.Letterboxd;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Search;
using Pufferfin.Catalog.Tmdb;

namespace Pufferfin.Catalog.Tests.TestSupport;

/// <summary>
/// The catalog wired through its real DI registration, with fake HTTP handlers, a fake library and a temporary cache directory.
/// </summary>
public sealed class CatalogTestHost : IDisposable, ICatalogSettingsProvider, ILetterboxdAccountProvider
{
    /// <summary>
    /// A TMDB v3 key: it travels in the query string, the worst case for leaks.
    /// </summary>
    public const string TmdbKey = "5ec7e7a1b0c0ffee5ec7e7a1b0c0ffee";

    private readonly bool _ownsDirectory;

    public CatalogTestHost(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? tmdb = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? audible = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? letterboxd = null,
        string? cacheDirectory = null,
        IBookCatalog? books = null)
    {
        static Task<HttpResponseMessage> NotFound(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.NotFound));

        Tmdb = new FakeHttpHandler(tmdb ?? NotFound);
        Audible = new FakeHttpHandler(audible ?? NotFound);
        Letterboxd = new FakeHttpHandler(letterboxd ?? NotFound);
        _ownsDirectory = cacheDirectory is null;
        CacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "pufferfin-catalog-tests", Guid.NewGuid().ToString("N"));
        CatalogSettings = new CatalogSettings
        {
            TmdbApiKey = TmdbKey,
            TmdbLanguages = ["fr-FR", "en-US"],
            AudibleMarkets = ["fr"]
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICatalogSettingsProvider>(this);
        services.AddSingleton<ILetterboxdAccountProvider>(this);
        if (books is not null)
        {
            services.AddSingleton(books);
        }

        services.AddLogging();
        services.AddSingleton<ILoggerFactory>(Logs);
        services.AddPufferfinCatalog();

        // The production budget, refilled hourly instead of every second: a slow runner must not get searches back mid-test.
        services.AddSingleton(_ => new SearchRateLimiter(SearchRateLimiter.BurstSize, SearchRateLimiter.SearchesPerSecond, TimeSpan.FromHours(1)));

        services.AddSingleton<ILocalCatalogIndex>(Library);
        services.AddSingleton<ICatalogCache>(sp => new CatalogCache(
            sp.GetRequiredService<IMemoryCache>(),
            CacheDirectory,
            TimeProvider.System,
            sp.GetRequiredService<ILogger<CatalogCache>>()));
        services.AddHttpClient(TmdbClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Tmdb);
        services.AddHttpClient(AudibleCatalogService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Audible);
        services.AddHttpClient(LetterboxdClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Letterboxd);
        Services = services.BuildServiceProvider();
    }

    public FakeHttpHandler Tmdb { get; }

    public FakeHttpHandler Audible { get; }

    public FakeHttpHandler Letterboxd { get; }

    public FakeLocalIndex Library { get; } = new();

    public CapturingLogger Logs { get; } = new();

    public string CacheDirectory { get; }

    public CatalogSettings CatalogSettings { get; set; }

    public ServiceProvider Services { get; }

    /// <summary>
    /// Gets or sets the Letterboxd account of every user of the test.
    /// </summary>
    public string? LetterboxdUsername { get; set; } = "cinephile";

    public CatalogSettings GetSettings() => CatalogSettings;

    public string? GetUsername(Guid userId) => LetterboxdUsername;

    public T Get<T>()
        where T : notnull
        => Services.GetRequiredService<T>();

    public void Dispose()
    {
        Services.Dispose();
        if (_ownsDirectory && Directory.Exists(CacheDirectory))
        {
            Directory.Delete(CacheDirectory, true);
        }
    }
}
