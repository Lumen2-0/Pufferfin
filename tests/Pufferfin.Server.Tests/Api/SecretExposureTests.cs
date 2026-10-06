using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Extensions.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Api;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;
using Pufferfin.Server.Tests.TestHelpers;
using Pufferfin.Server.Users;
using Xunit;

namespace Pufferfin.Server.Tests.Api;

/// <summary>
/// Secrets are write only: no endpoint returns one, and none reaches a log.
/// Every public action of every Pufferfin controller is called, so a new endpoint is covered by default.
/// </summary>
public sealed class SecretExposureTests : IDisposable
{
    private static readonly Dictionary<string, string> _secrets = new()
    {
        [SettingKeys.RadarrApiKey] = "SECRET-radarr-4f1c",
        [SettingKeys.SonarrApiKey] = "SECRET-sonarr-9a2e",
        [SettingKeys.ProwlarrApiKey] = "SECRET-prowlarr-77b0",
        [SettingKeys.QBittorrentPassword] = "SECRET-qbit-pass-31d8",
        [SettingKeys.BazarrApiKey] = "SECRET-bazarr-c0de",
        [SettingKeys.TmdbApiKey] = "SECRET-tmdb-0b5e",
    };

    private readonly StoreTestContext _context = new();
    private readonly StubHttpMessageHandler _handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") }));
    private readonly PufferfinConfigurationStore _store;
    private readonly AutoConfigRunner _runner;

    public SecretExposureTests()
    {
        // One secret comes from the environment, the others from the admin interface.
        _store = _context.Start(new Dictionary<string, string> { [SettingKeys.TmdbApiKey] = _secrets[SettingKeys.TmdbApiKey] });
        var changes = _secrets
            .Where(s => s.Key != SettingKeys.TmdbApiKey)
            .Select(s => (s.Key, (object?)s.Value))
            .Concat(
            [
                (SettingKeys.RadarrUrl, "http://radarr:7878"),
                (SettingKeys.SonarrUrl, "http://sonarr:8989"),
                (SettingKeys.ProwlarrUrl, "http://prowlarr:9696"),
                (SettingKeys.QBittorrentUrl, "http://qbittorrent:8080"),
                (SettingKeys.QBittorrentUsername, "admin"),
                (SettingKeys.BazarrUrl, "http://bazarr:6767"),
            ])
            .ToArray();
        _store.Update(StoreTestContext.Changes(changes));
        _runner = new AutoConfigRunner([], _store, TimeProvider.System, _context.Logs.CreateLogger<AutoConfigRunner>());
    }

    public void Dispose()
    {
        _runner.Dispose();
        _handler.Dispose();
        _context.Dispose();
    }

    [Fact]
    public async Task NoEndpoint_ReturnsASecret()
    {
        var controllerTypes = typeof(PufferfinConfigurationController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();
        Assert.NotEmpty(controllerTypes);

        var calls = 0;
        foreach (var controllerType in controllerTypes)
        {
            var controller = CreateController(controllerType);
            foreach (var action in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var arguments in ArgumentSets(action))
                {
                    var json = await InvokeAndSerializeAsync(controller, action, arguments);
                    calls++;
                    foreach (var secret in _secrets.Values)
                    {
                        Assert.False(json.Contains(secret, StringComparison.Ordinal), $"{controllerType.Name}.{action.Name} returned a secret.");
                    }
                }
            }
        }

        Assert.True(calls >= 6);
    }

    [Fact]
    public void ReadDto_ReportsSecretsAsSet_WithoutTheirValue()
    {
        var dto = PufferfinConfigurationDto.From(_store.Current);

        foreach (var setting in dto.Settings.Where(s => s.IsSecret))
        {
            Assert.Null(setting.Value);
            Assert.True(setting.IsSet, setting.Key);
        }

        Assert.True(dto.Settings.Single(s => s.Key == SettingKeys.TmdbApiKey).IsReadOnly);
    }

    [Fact]
    public void RejectedUpdate_DoesNotEchoTheSubmittedSecret()
    {
        var controller = (PufferfinConfigurationController)CreateController(typeof(PufferfinConfigurationController));
        var update = new UpdatePufferfinConfigurationDto
        {
            Settings = StoreTestContext.Changes(
                (SettingKeys.SonarrUrl, "http://admin:SECRET-in-url@sonarr"),
                (SettingKeys.TmdbApiKey, "SECRET-new-tmdb"))
        };

        var result = controller.UpdatePufferfinConfiguration(update);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var json = JsonSerializer.Serialize(badRequest.Value, JsonDefaults.Options);
        Assert.DoesNotContain("SECRET", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoLog_ContainsASecret()
    {
        var tester = new ServiceConnectionTester(_handler, _context.Logs.CreateLogger<ServiceConnectionTester>());
        foreach (var service in Enum.GetValues<PufferfinService>())
        {
            await tester.TestAsync(service, _store.Current.Settings, CancellationToken.None);
        }

        _context.Start(new Dictionary<string, string> { [SettingKeys.RadarrApiKey] = "SECRET-from-env-ignored" });

        Assert.NotEmpty(_context.Logs.Entries);
        foreach (var entry in _context.Logs.Entries)
        {
            Assert.DoesNotContain("SECRET", entry, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<object?[]> ArgumentSets(MethodInfo action)
    {
        var parameters = action.GetParameters();
        var services = parameters.Any(p => p.ParameterType == typeof(PufferfinService))
            ? Enum.GetValues<PufferfinService>()
            : [PufferfinService.Radarr];

        foreach (var service in services)
        {
            yield return parameters.Select(p => p.ParameterType switch
            {
                var t when t == typeof(PufferfinService) => (object?)service,
                var t when t == typeof(CancellationToken) => CancellationToken.None,
                var t when t == typeof(PufferfinUserSettingsDto) => new PufferfinUserSettingsDto { LetterboxdUsername = "cinephile" },
                var t when t == typeof(UpdatePufferfinConfigurationDto) => new UpdatePufferfinConfigurationDto
                {
                    Settings = StoreTestContext.Changes((SettingKeys.SeedMinDays, 2))
                },
                _ => throw new InvalidOperationException($"Add a case for {p.ParameterType.Name} in {action.Name} so the endpoint is covered.")
            }).ToArray();
        }
    }

    private static async Task<string> InvokeAndSerializeAsync(ControllerBase controller, MethodInfo action, object?[] arguments)
    {
        var returned = action.Invoke(controller, arguments);
        if (returned is Task task)
        {
            await task;
            returned = task.GetType().GetProperty("Result")!.GetValue(task);
        }

        // ActionResult<T> carries either a value or an IActionResult.
        var value = returned?.GetType().GetProperty("Value")?.GetValue(returned)
            ?? (returned?.GetType().GetProperty("Result")?.GetValue(returned) as ObjectResult)?.Value;
        return JsonSerializer.Serialize(value, JsonDefaults.Options);
    }

    private ControllerBase CreateController(Type controllerType)
    {
        if (controllerType == typeof(PufferfinConfigurationController))
        {
            var tester = new ServiceConnectionTester(_handler, _context.Logs.CreateLogger<ServiceConnectionTester>());
            return new PufferfinConfigurationController(_store, tester, _runner);
        }

        if (controllerType == typeof(PufferfinCapabilitiesController))
        {
            return new PufferfinCapabilitiesController(_store);
        }

        if (controllerType == typeof(PufferfinUserSettingsController))
        {
            var userStore = new PufferfinUserSettingsStore(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_context.FilePath)!, "pufferfin-users.json"),
                _context.Logs.CreateLogger<PufferfinUserSettingsStore>());
            return new PufferfinUserSettingsController(userStore)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, Guid.NewGuid().ToString("N"))], "Test"))
                    }
                }
            };
        }

        throw new InvalidOperationException($"Add {controllerType.Name} to this test so its endpoints are covered.");
    }
}
