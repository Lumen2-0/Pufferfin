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
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Connectivity;
using Pufferfin.Server.Deletion;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Storage;
using Pufferfin.Server.Tests.Requests;
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
        [SettingKeys.ShelfmarkApiKey] = "SECRET-shelfmark-1a2b",
    };

    private readonly StoreTestContext _context = new();
    private readonly StubHttpMessageHandler _handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") }));
    private readonly PufferfinConfigurationStore _store;
    private readonly AutoConfigRunner _runner;
    private Guid _requestId = Guid.NewGuid();

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
                (SettingKeys.ShelfmarkUrl, "http://shelfmark:8084"),
            ])
            .ToArray();
        _store.Update(StoreTestContext.Changes(changes));
        _runner = new AutoConfigRunner([], _store, TimeProvider.System, _context.Logs.CreateLogger<AutoConfigRunner>());
    }

    private RequestStore Requests => field ??= new RequestStore(
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_context.FilePath)!, "pufferfin-requests.json"),
        _context.Logs.CreateLogger<RequestStore>());

    private PufferfinUserSettingsStore UserSettings => field ??= new PufferfinUserSettingsStore(
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_context.FilePath)!, "pufferfin-users.json"),
        _context.Logs.CreateLogger<PufferfinUserSettingsStore>());

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
                foreach (var arguments in ArgumentSets(action, _requestId))
                {
                    var json = await InvokeAndSerializeAsync(controller, action, arguments);
                    calls++;
                    foreach (var secret in _secrets.Values.Append(Requests.GetWebhookToken()))
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

    private static IEnumerable<object?[]> ArgumentSets(MethodInfo action, Guid requestId)
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
                var t when t == typeof(Guid) => requestId,
                var t when t == typeof(bool) => true,
                var t when t == typeof(int) => 1399,
                var t when t == typeof(CreateMediaRequestDto) => new CreateMediaRequestDto { Kind = RequestKind.Episode, TmdbId = 1399, SeasonNumber = 1, EpisodeNumber = 1 },
                var t when t == typeof(MoveMediaRequestDto) => new MoveMediaRequestDto { Index = 0 },
                var t when t == typeof(RequestPermissionsDto) => new RequestPermissionsDto { WeeklyQuota = 5 },
                var t when t == typeof(System.Text.Json.Nodes.JsonObject) => new System.Text.Json.Nodes.JsonObject { ["eventType"] = "Test" },
                var t when t == typeof(DeletionMode) => DeletionMode.Immediate,
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
            return new PufferfinUserSettingsController(UserSettings)
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

        if (controllerType == typeof(PufferfinRequestsController))
        {
            var service = new RequestService(Requests, new NoWaker(), _store, UserSettings, new RequestTestKit.FakeMetadata(), new RequestTestKit.FakeAudiobooks(), TimeProvider.System);
            _requestId = service.CreateAsync(Guid.NewGuid(), true, new RequestInput(RequestKind.Movie, 438631), CancellationToken.None).GetAwaiter().GetResult().Request!.Id;
            return new PufferfinRequestsController(service, Requests) { ControllerContext = AdministratorContext() };
        }

        if (controllerType == typeof(PufferfinWebhookController))
        {
            var context = AdministratorContext();
            context.HttpContext.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("pufferfin:" + Requests.GetWebhookToken()));
            return new PufferfinWebhookController(Requests, new NoWaker(), new RequestTestKit.FakeLibrary(), _context.Logs.CreateLogger<PufferfinWebhookController>()) { ControllerContext = context };
        }

        if (controllerType == typeof(PufferfinAudiobooksController))
        {
            var tester = new ServiceConnectionTester(_handler, _context.Logs.CreateLogger<ServiceConnectionTester>());
            var qBittorrent = new QBittorrentClient(_handler, _context.Logs.CreateLogger<QBittorrentClient>());
            var links = new Seeding.SeedingTestKit.FakeFileLinks();
            var library = new AudiobookLibrary(_handler, links, () => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_context.FilePath)!, "audiobooks"), _context.Logs.CreateLogger<AudiobookLibrary>());
            var hardlinks = new HardlinkCheck(links, TimeProvider.System, _context.Logs.CreateLogger<HardlinkCheck>());
            return new PufferfinAudiobooksController(_store, new AudiobookHealthCheck(tester, qBittorrent, library, hardlinks, TimeProvider.System));
        }

        if (controllerType == typeof(PufferfinStorageController) || controllerType == typeof(PufferfinLibraryController))
        {
            var links = new Seeding.SeedingTestKit.FakeFileLinks();
            var qBittorrent = new QBittorrentClient(_handler, _context.Logs.CreateLogger<QBittorrentClient>());
            var arr = new ArrClient(new ArrHttpClient(_handler, _context.Logs.CreateLogger<ArrHttpClient>()));
            var seeding = new SeedingStore(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_context.FilePath)!, "pufferfin-seeding.json"), _context.Logs.CreateLogger<SeedingStore>());
            var runner = new SeedPolicyRunner(_store, qBittorrent, arr, seeding, links, TimeProvider.System, _context.Logs.CreateLogger<SeedPolicyRunner>());
            if (controllerType == typeof(PufferfinStorageController))
            {
                var hardlinks = new HardlinkCheck(links, TimeProvider.System, _context.Logs.CreateLogger<HardlinkCheck>());
                var storage = new StorageService(_store, qBittorrent, arr, seeding, runner, hardlinks, links, new DriveDiskSpace(), TimeProvider.System, _context.Logs.CreateLogger<StorageService>());
                return new PufferfinStorageController(storage, runner) { ControllerContext = AdministratorContext() };
            }

            var resolver = new TitleTorrentResolver(arr, qBittorrent, new RequestTestKit.FakeMetadata(), links, _context.Logs.CreateLogger<TitleTorrentResolver>());
            var deletion = new ContentDeletionService(
                new AnyMovieLibrary(),
                new RequestTestKit.FakeLibrary(),
                resolver,
                arr,
                qBittorrent,
                seeding,
                Requests,
                _store,
                TimeProvider.System,
                _context.Logs.CreateLogger<ContentDeletionService>());
            return new PufferfinLibraryController(deletion) { ControllerContext = AdministratorContext() };
        }

        throw new InvalidOperationException($"Add {controllerType.Name} to this test so its endpoints are covered.");
    }

    private static ControllerContext AdministratorContext()
        => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(InternalClaimTypes.UserId, Guid.NewGuid().ToString("N")), new Claim(ClaimTypes.Role, UserRoles.Administrator)],
                    "Test"))
            }
        };

    private sealed class NoWaker : IRequestWaker
    {
        public void Wake()
        {
        }
    }

    private sealed class AnyMovieLibrary : ILibraryContent
    {
        public LibraryTitle? Find(Guid itemId, Guid? visibleTo) => new(itemId, LibraryTitleKind.Movie, "Dune", 438631, null, null, null, []);

        public IReadOnlyList<Guid> FindMovies(int tmdbId) => [];

        public void DeleteFiles(Guid itemId)
        {
        }
    }
}
