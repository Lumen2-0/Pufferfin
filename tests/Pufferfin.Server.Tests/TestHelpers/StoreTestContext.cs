using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Tests.TestHelpers;

/// <summary>
/// A configuration directory on disk. Creating a second store on the same directory simulates a restart.
/// </summary>
public sealed class StoreTestContext : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));

    public StoreTestContext()
    {
        Directory.CreateDirectory(_directory);
    }

    public CapturingLoggerProvider Logs { get; } = new();

    public string FilePath => Path.Combine(_directory, "pufferfin.json");

    public static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    public static Dictionary<string, JsonElement> Changes(params (string Key, object? Value)[] changes)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in changes)
        {
            result[key] = Json(value);
        }

        return result;
    }

    /// <summary>
    /// Starts the server: reads the file and applies the environment.
    /// </summary>
    /// <param name="environment">The environment variables of this start.</param>
    /// <returns>The store.</returns>
    public PufferfinConfigurationStore Start(IReadOnlyDictionary<string, string>? environment = null)
        => new(
            new SettingsFile(FilePath, Logs.CreateLogger<SettingsFile>()),
            new FakeEnvironment(environment ?? new Dictionary<string, string>()),
            Logs.CreateLogger<PufferfinConfigurationStore>());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
