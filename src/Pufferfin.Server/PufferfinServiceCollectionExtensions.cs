using System;
using System.IO;
using System.Net.Http;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;

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
    /// Adds the Pufferfin configuration store, connection tests and auto-configuration replay.
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

        return services;
    }
}
