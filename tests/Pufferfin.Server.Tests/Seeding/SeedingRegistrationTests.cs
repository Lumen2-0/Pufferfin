using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pufferfin.Server.Deletion;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Storage;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// The server can build every service of the seeding policy, the deletion and the storage screen.
/// </summary>
public sealed class SeedingRegistrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AddPufferfin_BuildsTheSeedingDeletionAndStorageServices()
    {
        Directory.CreateDirectory(_directory);
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(Stub<IApplicationPaths>((method, _) => method.ReturnType == typeof(string) ? _directory : null));
        services.AddSingleton(Stub<ILibraryManager>((_, _) => null));
        services.AddSingleton(Stub<ILibraryMonitor>((_, _) => null));
        services.AddSingleton(Stub<IServerApplicationHost>((_, _) => null));
        services.AddPufferfin();

        // The Jellyfin services the other Pufferfin modules need: answered by stubs, they are never called here.
        foreach (var type in services.Where(d => d.ImplementationType is not null)
            .SelectMany(d => d.ImplementationType!.GetConstructors().SelectMany(c => c.GetParameters()))
            .Select(p => p.ParameterType)
            .Where(t => t.IsInterface && (t.Namespace?.StartsWith("MediaBrowser", StringComparison.Ordinal) == true || t.Namespace?.StartsWith("Jellyfin", StringComparison.Ordinal) == true))
            .Distinct()
            .Where(t => !services.Any(d => d.ServiceType == t))
            .ToList())
        {
            services.AddSingleton(type, typeof(SeedingRegistrationTests).GetMethod(nameof(Stub), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, [(Func<MethodInfo, object?[]?, object?>)((_, _) => null)])!);
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<SeedPolicyRunner>());
        Assert.NotNull(provider.GetRequiredService<StorageService>());
        Assert.NotNull(provider.GetRequiredService<ContentDeletionService>());
        Assert.IsType<NativeFileLinks>(provider.GetRequiredService<IFileLinks>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is SeedPolicyService);
    }

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> answer)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, StubProxy>();
        ((StubProxy)(object)proxy).Answer = answer;
        return proxy;
    }

    /// <summary>
    /// An interface implementation answering every call from a function.
    /// </summary>
    public class StubProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Answer { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is null ? null : Answer(targetMethod, args);
    }
}
