using System;
using System.IO;
using System.Net.Http;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pufferfin.Catalog;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.Catalog;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;
using Pufferfin.Server.Users;

namespace Pufferfin.Server;

/// <summary>
/// Registers the Pufferfin services.
/// </summary>
public static class PufferfinServiceCollectionExtensions
{
    /// <summary>
    /// The name of the store file, in the server configuration directory.
    /// </summary>
    public const string SettingsFileName = "pufferfin.json";

    /// <summary>
    /// The name of the per user settings file, in the server configuration directory.
    /// </summary>
    public const string UserSettingsFileName = "pufferfin-users.json";

    /// <summary>
    /// Adds the Pufferfin configuration store, connection tests, auto-configuration replay, per user settings and the catalog.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPufferfin(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEnvironmentReader, ProcessEnvironmentReader>();
        services.AddSingleton(provider => new SettingsFile(
            Path.Combine(provider.GetRequiredService<IApplicationPaths>().ConfigurationDirectoryPath, SettingsFileName),
            provider.GetRequiredService<ILogger<SettingsFile>>()));
        services.AddSingleton<IPufferfinConfigurationStore, PufferfinConfigurationStore>();

        // Request logging is removed: a TMDB v3 key travels in the query string.
        services.AddHttpClient(ServiceConnectionTester.HttpClientName, client => client.Timeout = ServiceConnectionTester.DefaultTimeout + TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = ServiceConnectionTester.DefaultTimeout })
            .RemoveAllLoggers();
        services.AddSingleton<IServiceConnectionTester, ServiceConnectionTester>();

        services.AddSingleton<IAutoConfigRunner, AutoConfigRunner>();
        services.AddHostedService<AutoConfigReplayService>();

        services.AddSingleton<IPufferfinUserSettingsStore>(provider => new PufferfinUserSettingsStore(
            Path.Combine(provider.GetRequiredService<IApplicationPaths>().ConfigurationDirectoryPath, UserSettingsFileName),
            provider.GetRequiredService<ILogger<PufferfinUserSettingsStore>>()));

        // The catalog reads the store, not the environment, and the Letterboxd account each user set for themselves.
        // Registered before AddPufferfinCatalog, whose environment based fallbacks are TryAdd.
        services.AddSingleton<ICatalogSettingsProvider, StoreCatalogSettingsProvider>();
        services.AddSingleton<ILetterboxdAccountProvider, StoreLetterboxdAccountProvider>();
        services.AddPufferfinCatalog();

        return services;
    }
}
