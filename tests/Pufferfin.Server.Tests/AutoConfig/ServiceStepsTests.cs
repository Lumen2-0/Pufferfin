using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Arr;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.AutoConfig.Steps;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.AutoConfig;

public sealed class ServiceStepsTests
{
    // qBittorrent52Login: the login answer of qBittorrent 5.2 and later (204, empty body, QBT_SID_<port> cookie).
    private static FakeArrServer QBittorrent(bool autoTmm = false, bool qBittorrent52Login = false)
    {
        var server = new FakeArrServer();
        var categories = new JsonObject();
        server.Objects["torrents/categories"] = categories;
        server.Objects["app/preferences"] = new JsonObject { ["auto_tmm_enabled"] = autoTmm, ["save_path"] = "/downloads" };
        server.Overrides.Add((method, path, body) =>
        {
            if (method == HttpMethod.Post && path == "auth/login")
            {
                if (qBittorrent52Login)
                {
                    var noContent = new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent(string.Empty) };
                    noContent.Headers.Add("Set-Cookie", "QBT_SID_8080=abc123; HttpOnly; SameSite=Lax; path=/");
                    return noContent;
                }

                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", "SID=abc123; HttpOnly; path=/");
                return login;
            }

            if (method == HttpMethod.Post && path == "torrents/createCategory")
            {
                var name = Uri.UnescapeDataString(body!.Split('&')[0].Split('=')[1]);
                ((JsonObject)server.Objects["torrents/categories"])[name] = new JsonObject { ["name"] = name, ["savePath"] = string.Empty };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
            }

            if (method == HttpMethod.Post && path == "app/setPreferences")
            {
                var json = JsonNode.Parse(Uri.UnescapeDataString(body!["json=".Length..].Replace('+', ' ')))!.AsObject();
                var preferences = (JsonObject)server.Objects["app/preferences"];
                foreach (var (key, value) in json)
                {
                    preferences[key] = value?.DeepClone();
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
            }

            return null;
        });
        return server;
    }

    private static QBittorrentStep QBittorrentStep(FakeArrServer server)
        => new(new QBittorrentClient(server, NullLogger<QBittorrentClient>.Instance, TimeSpan.FromSeconds(5)));

    [Fact]
    public async Task QBittorrent_FirstSetup_CreatesTheCategories_AndTurnsOnTheAutomaticManagement()
    {
        var server = QBittorrent();

        var outcome = await QBittorrentStep(server).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, outcome.Status);
        var categories = (JsonObject)server.Objects["torrents/categories"];
        Assert.True(categories.ContainsKey(MediaLayout.RadarrCategory));
        Assert.True(categories.ContainsKey(MediaLayout.SonarrCategory));
        Assert.True(server.Objects["app/preferences"]["auto_tmm_enabled"]!.GetValue<bool>());

        // The session cookie of the login is sent with the next calls, the password never is.
        var call = server.Calls.Single(c => c.Path == "torrents/categories");
        Assert.Contains("SID=abc123", call.Request.Headers.GetValues("Cookie").Single(), StringComparison.Ordinal);
        Assert.All(server.Calls.Where(c => c.Path != "auth/login"), c => Assert.DoesNotContain(ArrFixtures.QBittorrentPassword, c.Body ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task QBittorrent52_Login_NoContentAndPortCookie_IsAccepted()
    {
        var server = QBittorrent(qBittorrent52Login: true);

        var outcome = await QBittorrentStep(server).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, outcome.Status);
        var call = server.Calls.Single(c => c.Path == "torrents/categories");
        Assert.Contains("QBT_SID_8080=abc123", call.Request.Headers.GetValues("Cookie").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QBittorrent_WithShelfmark_CreatesTheAudiobooksCategory_EvenWithoutRadarrNorSonarr()
    {
        var server = QBittorrent();
        var settings = ArrFixtures.Settings(
            (SettingKeys.QBittorrentUrl, "http://qbittorrent:8080"),
            (SettingKeys.QBittorrentUsername, "admin"),
            (SettingKeys.QBittorrentPassword, ArrFixtures.QBittorrentPassword),
            (SettingKeys.ShelfmarkUrl, "http://shelfmark:8084"));

        var outcome = await QBittorrentStep(server).ApplyAsync(settings, CancellationToken.None);
        var again = await QBittorrentStep(server).ApplyAsync(settings, CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, outcome.Status);
        Assert.Equal(AutoConfigStepStatus.Unchanged, again.Status);
        var categories = (JsonObject)server.Objects["torrents/categories"];
        Assert.True(categories.ContainsKey(MediaLayout.AudiobooksCategory));
        Assert.False(categories.ContainsKey(MediaLayout.RadarrCategory));
    }

    [Fact]
    public async Task QBittorrent_AutomaticManagementTurnedOffByHand_StaysOff()
    {
        var server = QBittorrent();
        var step = QBittorrentStep(server);
        await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);
        server.Objects["app/preferences"]["auto_tmm_enabled"] = false;

        var outcome = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Unchanged, outcome.Status);
        Assert.False(server.Objects["app/preferences"]["auto_tmm_enabled"]!.GetValue<bool>());
        Assert.Single(server.Calls, c => c.Path == "app/setPreferences");
    }

    [Fact]
    public async Task QBittorrent_RefusingTheCredentials_FailsWithoutLeakingThem()
    {
        var server = new FakeArrServer();
        server.Overrides.Add((_, path, _) => path == "auth/login" ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") } : null);

        var outcome = await QBittorrentStep(server).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Failed, outcome.Status);
        Assert.Contains("refused", outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(ArrFixtures.QBittorrentPassword, outcome.Detail, StringComparison.Ordinal);
    }

    private static FakeArrServer Prowlarr()
    {
        var server = new FakeArrServer();
        server.Resources["applications"] = [];
        server.Resources["indexerProxy"] = [];
        server.Resources["tag"] = [];
        server.Resources["indexerstatus"] = (JsonArray)JsonNode.Parse("[{\"indexerId\":5},{\"indexerId\":6}]")!;
        server.Resources["indexer"] = (JsonArray)JsonNode.Parse("[{\"id\":5,\"name\":\"Protected\",\"tags\":[]},{\"id\":6,\"name\":\"Broken\",\"tags\":[]},{\"id\":7,\"name\":\"Healthy\",\"tags\":[]}]")!;
        server.Schemas["applications"] = (JsonArray)JsonNode.Parse("""
            [{"implementation":"Radarr","fields":[{"name":"prowlarrUrl","value":""},{"name":"baseUrl","value":""},{"name":"apiKey","value":""}]},
             {"implementation":"Sonarr","fields":[{"name":"prowlarrUrl","value":""},{"name":"baseUrl","value":""},{"name":"apiKey","value":""}]}]
            """)!;
        server.Schemas["indexerProxy"] = (JsonArray)JsonNode.Parse("[{\"implementation\":\"FlareSolverr\",\"fields\":[{\"name\":\"host\",\"value\":\"\"},{\"name\":\"requestTimeout\",\"value\":60}]}]")!;
        server.Overrides.Add((method, path, body) =>
        {
            if (method != HttpMethod.Post || path != "indexer/test")
            {
                return null;
            }

            var id = JsonNode.Parse(body!)!["id"]!.GetValue<int>();
            var message = id == 5 ? "Unable to access protected.example, blocked by CloudFlare Protection." : "Unable to connect to indexer";
            return FakeArrServer.Json(JsonNode.Parse("[{\"errorMessage\":\"" + message + "\"}]"), HttpStatusCode.BadRequest);
        });
        return server;
    }

    [Fact]
    public async Task Prowlarr_DeclaresRadarrAndSonarr_AndRoutesOnlyCloudflareIndexersThroughFlareSolverr()
    {
        var server = Prowlarr();
        var step = new ProwlarrStep(new ProwlarrClient(FakeArrServer.CreateHttp(server)));

        var first = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, first.Status);
        var applications = server.Resource("applications").OfType<JsonObject>().ToList();
        Assert.Equal(new[] { "Radarr", "Sonarr" }, applications.Select(a => a["implementation"]!.GetValue<string>()));
        var radarr = applications[0];
        Assert.Equal("http://radarr:7878", ArrFields.Get(radarr, "baseUrl")!.GetValue<string>());
        Assert.Equal(ArrFixtures.RadarrKey, ArrFields.Get(radarr, "apiKey")!.GetValue<string>());

        var tagId = Assert.Single(server.Resource("tag"))!["id"]!.GetValue<int>();
        var proxy = Assert.Single(server.Resource("indexerProxy"))!.AsObject();
        Assert.Equal(tagId, proxy["tags"]![0]!.GetValue<int>());
        var indexers = server.Resource("indexer").OfType<JsonObject>().ToDictionary(i => i["name"]!.GetValue<string>());
        Assert.Contains(indexers["Protected"]["tags"]!.AsArray(), t => t!.GetValue<int>() == tagId);
        Assert.Empty(indexers["Broken"]["tags"]!.AsArray());
        Assert.Empty(indexers["Healthy"]["tags"]!.AsArray());

        var writes = server.Writes.Count(w => w.Path != "indexer/test");
        var second = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);
        Assert.Equal(AutoConfigStepStatus.Unchanged, second.Status);
        Assert.Equal(writes, server.Writes.Count(w => w.Path != "indexer/test"));
    }

    [Fact]
    public async Task Prowlarr_GivesAnimeSearchesTheSeriesAndAdultCategories_Once()
    {
        var server = Prowlarr();
        server.Add("applications", (JsonObject)JsonNode.Parse(
            "{\"name\":\"Sonarr\",\"implementation\":\"Sonarr\",\"fields\":[{\"name\":\"syncCategories\",\"value\":[5000,5040]},{\"name\":\"animeSyncCategories\",\"value\":[5070]}]}")!);
        server.Resources["command"] = [];
        var step = new ProwlarrStep(new ProwlarrClient(FakeArrServer.CreateHttp(server)));

        await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        var sonarr = server.Resource("applications").OfType<JsonObject>().Single(a => a["implementation"]!.GetValue<string>() == "Sonarr");
        Assert.Equal(new[] { 5000, 5040, 5070, 6000, 6070 }, ArrFields.Get(sonarr, "animeSyncCategories")!.AsArray().Select(c => c!.GetValue<int>()));
        Assert.Contains(server.Resource("command"), c => c!["name"]!.GetValue<string>() == "ApplicationIndexerSync");

        var writes = server.Writes.Count(w => w.Path != "indexer/test");
        await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);
        Assert.Equal(writes, server.Writes.Count(w => w.Path != "indexer/test"));
    }

    [Fact]
    public async Task Prowlarr_ApplicationDeclaredByHand_IsNotDuplicated()
    {
        var server = Prowlarr();
        server.Add("applications", new JsonObject { ["name"] = "My Radarr", ["implementation"] = "Radarr" });

        await new ProwlarrStep(new ProwlarrClient(FakeArrServer.CreateHttp(server))).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Single(server.Resource("applications"), a => a!["implementation"]!.GetValue<string>() == "Radarr");
    }

    [Fact]
    public async Task Bazarr_IsConnectedOnce_AndAConnectionMadeByHandIsKept()
    {
        var server = new FakeArrServer();
        server.Objects["system/settings"] = JsonNode.Parse("{\"general\":{\"use_radarr\":false,\"use_sonarr\":true},\"radarr\":{},\"sonarr\":{\"ip\":\"my-sonarr\"}}")!;
        var step = new BazarrStep(new BazarrClient(FakeArrServer.CreateHttp(server)));

        var outcome = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, outcome.Status);
        var form = Assert.Single(server.Writes).Body!;
        Assert.Contains("settings-general-use_radarr=true", form, StringComparison.Ordinal);
        Assert.Contains("settings-radarr-ip=radarr", form, StringComparison.Ordinal);
        Assert.Contains("settings-radarr-port=7878", form, StringComparison.Ordinal);
        Assert.DoesNotContain("settings-sonarr", form, StringComparison.Ordinal);
        Assert.DoesNotContain("use_sonarr", form, StringComparison.Ordinal);
    }
}
