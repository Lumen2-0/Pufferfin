using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Server.Arr;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.AutoConfig.Steps;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.AutoConfig;

public sealed class ArrStepsTests
{
    private static ArrClient Client(FakeArrServer server) => new(FakeArrServer.CreateHttp(server));

    private static JsonObject Named(FakeArrServer server, string resource, string name)
        => server.Resource(resource).OfType<JsonObject>().Single(i => i["name"]?.GetValue<string>() == name);

    [Fact]
    public async Task LanguageProfiles_AreCreatedOnce_ThenTheReplayChangesNothing()
    {
        var server = ArrFixtures.RadarrLike();
        var step = new LanguageProfilesStep(Client(server), PufferfinService.Radarr);
        var settings = ArrFixtures.FullStack("fr,en");

        var first = await step.ApplyAsync(settings, CancellationToken.None);
        var writesAfterFirst = server.Writes.Count();
        var second = await step.ApplyAsync(settings, CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, first.Status);
        Assert.Equal(AutoConfigStepStatus.Unchanged, second.Status);
        Assert.Equal(writesAfterFirst, server.Writes.Count());
        Assert.Equal(
            new[] { "Pufferfin Original", "Pufferfin French", "Pufferfin English" },
            server.Resource("qualityprofile").Select(p => p!["name"]!.GetValue<string>()));
        Assert.Contains(server.Resource("customformat"), f => f!["name"]!.GetValue<string>() == "Pufferfin subtitled only French");
        Assert.Contains(server.Resource("customformat"), f => f!["name"]!.GetValue<string>() == "Pufferfin audio Original");
    }

    [Fact]
    public async Task LanguageProfile_RequiresItsLanguage_AndRefusesSubtitledOnlyReleases()
    {
        var server = ArrFixtures.RadarrLike();
        await new LanguageProfilesStep(Client(server), PufferfinService.Radarr).ApplyAsync(ArrFixtures.FullStack("fr"), CancellationToken.None);

        var french = Named(server, "qualityprofile", "Pufferfin French");
        var audioFormat = Named(server, "customformat", "Pufferfin audio French");
        var subtitledFormat = Named(server, "customformat", "Pufferfin subtitled only French");
        int Score(JsonObject format) => french["formatItems"]!.AsArray().Single(i => i!["format"]!.GetValue<int>() == format["id"]!.GetValue<int>())!["score"]!.GetValue<int>();

        Assert.Equal(LanguageProfilesStep.LanguageScore, Score(audioFormat));
        Assert.Equal(LanguageProfilesStep.SubtitledOnlyScore, Score(subtitledFormat));
        Assert.Equal(LanguageProfilesStep.LanguageScore, french["minFormatScore"]!.GetValue<int>());
        Assert.True(Score(audioFormat) + Score(subtitledFormat) < french["minFormatScore"]!.GetValue<int>());
        Assert.False(french["upgradeAllowed"]!.GetValue<bool>());
        Assert.Equal(ArrLanguages.AnyId, french["language"]!["id"]!.GetValue<int>());

        // The French audio condition points to the French language id of the service.
        var languageCondition = audioFormat["specifications"]!.AsArray().Single()!;
        Assert.Equal(2, languageCondition["fields"]!.AsArray().Single(f => f!["name"]!.GetValue<string>() == "value")!["value"]!.GetValue<int>());

        // The subtitled only format is what the service evaluates: it must match a VOSTFR, not a MULTi VFF.
        var conditions = subtitledFormat["specifications"]!.AsArray().OfType<JsonObject>().ToList();
        var subtitled = new Regex(conditions.Single(c => !c["negate"]!.GetValue<bool>())["fields"]![0]!["value"]!.GetValue<string>(), RegexOptions.IgnoreCase);
        var audio = new Regex(conditions.Single(c => c["negate"]!.GetValue<bool>())["fields"]![0]!["value"]!.GetValue<string>(), RegexOptions.IgnoreCase);
        bool Matches(string title) => subtitled.IsMatch(title) && !audio.IsMatch(title);
        Assert.True(Matches("Film.2024.VOSTFR.1080p.WEB"));
        Assert.False(Matches("Film.2024.MULTi.VFF.1080p.WEB"));
        Assert.All(conditions, c => Assert.True(c["required"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task LanguageProfile_GroupsQualitiesByResolution_WithRemuxOnlyAtTheTopAndOff()
    {
        var server = ArrFixtures.RadarrLike();
        await new LanguageProfilesStep(Client(server), PufferfinService.Radarr).ApplyAsync(ArrFixtures.FullStack("fr"), CancellationToken.None);

        var items = Named(server, "qualityprofile", "Pufferfin Original")["items"]!.AsArray().OfType<JsonObject>().ToList();
        JsonObject Group(string name) => items.Single(i => i["name"]?.GetValue<string>() == name);
        string[] Members(JsonObject group) => group["items"]!.AsArray().Select(i => i!["quality"]!["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(new[] { "WEBDL-1080p", "WEBRip-1080p", "Bluray-1080p" }, Members(Group("1080p")));
        Assert.True(Group("1080p")["allowed"]!.GetValue<bool>());
        Assert.True(Group("720p")["allowed"]!.GetValue<bool>());
        Assert.False(Group("2160p")["allowed"]!.GetValue<bool>());
        Assert.Equal("Remux-2160p", items[^1]["quality"]!["name"]!.GetValue<string>());
        Assert.All(
            items.Where(i => i["quality"]?["name"]?.GetValue<string>()?.Contains("Remux", StringComparison.Ordinal) == true),
            i => Assert.False(i["allowed"]!.GetValue<bool>()));
        Assert.Equal(Group("1080p")["id"]!.GetValue<int>(), Named(server, "qualityprofile", "Pufferfin Original")["cutoff"]!.GetValue<int>());
    }

    [Fact]
    public async Task LanguageProfile_AdjustedByHand_IsNeverOverwritten()
    {
        var server = ArrFixtures.RadarrLike();
        server.Add("qualityprofile", new JsonObject { ["name"] = "Pufferfin French", ["minFormatScore"] = 42, ["upgradeAllowed"] = true });

        await new LanguageProfilesStep(Client(server), PufferfinService.Radarr).ApplyAsync(ArrFixtures.FullStack("fr"), CancellationToken.None);

        Assert.DoesNotContain(server.Writes, w => w.Method == HttpMethod.Put);
        var french = Named(server, "qualityprofile", "Pufferfin French");
        Assert.Equal(42, french["minFormatScore"]!.GetValue<int>());
        Assert.Single(server.Resource("qualityprofile"), p => p!["name"]!.GetValue<string>() == "Pufferfin French");
    }

    [Fact]
    public async Task LanguageProfile_WithoutSubtitleRefusal_HasNoSubtitledFormat()
    {
        var server = ArrFixtures.RadarrLike();
        var settings = ArrFixtures.Settings(
            (SettingKeys.RadarrUrl, "http://radarr:7878"),
            (SettingKeys.RadarrApiKey, ArrFixtures.RadarrKey),
            (SettingKeys.PreferredAudioLanguages, "fr"),
            (SettingKeys.RejectSubtitledOnly, false));

        await new LanguageProfilesStep(Client(server), PufferfinService.Radarr).ApplyAsync(settings, CancellationToken.None);

        Assert.DoesNotContain(server.Resource("customformat"), f => f!["name"]!.GetValue<string>().StartsWith("Pufferfin subtitled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RootFolders_AreCreatedForEachCategory_ThenLeftAlone()
    {
        var server = ArrFixtures.RadarrLike();
        var step = new RootFoldersStep(Client(server), PufferfinService.Radarr);

        var first = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);
        var second = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, first.Status);
        Assert.Equal(AutoConfigStepStatus.Unchanged, second.Status);
        Assert.Equal(MediaLayout.RadarrRootFolders.Values.Order(), server.Resource("rootfolder").Select(f => f!["path"]!.GetValue<string>()).Order());
    }

    [Fact]
    public async Task RootFolders_LaidOutByHand_AreRespected()
    {
        var server = ArrFixtures.RadarrLike();
        foreach (var path in new[] { "/data/Films", "/data/Anime", "/data/Documentaries" })
        {
            server.Add("rootfolder", new JsonObject { ["path"] = path });
        }

        var outcome = await new RootFoldersStep(Client(server), PufferfinService.Radarr).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Unchanged, outcome.Status);
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task RootFolder_RefusedByTheService_FailsWithAReadableReason()
    {
        var server = ArrFixtures.RadarrLike();
        server.Overrides.Add((method, path, _) => method == HttpMethod.Post && path == "rootfolder"
            ? FakeArrServer.Json(JsonNode.Parse("[{\"errorMessage\":\"Folder does not exist\"}]"), System.Net.HttpStatusCode.BadRequest)
            : null);

        var outcome = await new RootFoldersStep(Client(server), PufferfinService.Radarr).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Failed, outcome.Status);
        Assert.Contains("/media/movies", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadClient_IsDeclaredWithItsCategory_Once()
    {
        var server = ArrFixtures.RadarrLike();
        var step = new DownloadClientStep(Client(server), PufferfinService.Radarr);

        var first = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);
        var second = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, first.Status);
        Assert.Equal(AutoConfigStepStatus.Unchanged, second.Status);
        var client = Assert.Single(server.Resource("downloadclient"))!.AsObject();
        Assert.Equal("QBittorrent", client["implementation"]!.GetValue<string>());
        Assert.Equal("qbittorrent", ArrFields.Get(client, "host")!.GetValue<string>());
        Assert.Equal(8080, ArrFields.Get(client, "port")!.GetValue<int>());
        Assert.Equal(MediaLayout.RadarrCategory, ArrFields.Get(client, "movieCategory")!.GetValue<string>());
        Assert.True(client["enable"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DownloadClient_DeclaredByHand_IsLeftAsItIs()
    {
        var server = ArrFixtures.RadarrLike();
        server.Add("downloadclient", new JsonObject { ["name"] = "My qBit", ["implementation"] = "QBittorrent" });

        var outcome = await new DownloadClientStep(Client(server), PufferfinService.Radarr).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Unchanged, outcome.Status);
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task DownloadClient_WithoutQBittorrent_IsSkipped()
    {
        var server = ArrFixtures.RadarrLike();
        var settings = ArrFixtures.Settings((SettingKeys.RadarrUrl, "http://radarr:7878"), (SettingKeys.RadarrApiKey, ArrFixtures.RadarrKey));

        var outcome = await new DownloadClientStep(Client(server), PufferfinService.Radarr).ApplyAsync(settings, CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Skipped, outcome.Status);
        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task EveryArrStep_WithoutItsService_IsSkipped_AndCallsNothing()
    {
        var server = ArrFixtures.RadarrLike();
        var empty = ArrFixtures.Settings();
        var client = Client(server);
        IAutoConfigStep[] steps =
        [
            new RootFoldersStep(client, PufferfinService.Sonarr),
            new DownloadClientStep(client, PufferfinService.Sonarr),
            new LanguageProfilesStep(client, PufferfinService.Sonarr),
            new ProwlarrStep(new ProwlarrClient(FakeArrServer.CreateHttp(server))),
            new BazarrStep(new BazarrClient(FakeArrServer.CreateHttp(server))),
        ];

        foreach (var step in steps)
        {
            Assert.Equal(AutoConfigStepStatus.Skipped, (await step.ApplyAsync(empty, CancellationToken.None)).Status);
        }

        Assert.Empty(server.Calls);
    }

    [Fact]
    public async Task AFailedStep_NeverReportsASecret()
    {
        var server = ArrFixtures.RadarrLike();
        server.Overrides.Add((_, _, _) => FakeArrServer.Json(JsonNode.Parse("{\"message\":\"bad key " + ArrFixtures.RadarrKey + "\"}"), System.Net.HttpStatusCode.InternalServerError));

        var outcome = await new LanguageProfilesStep(Client(server), PufferfinService.Radarr).ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Failed, outcome.Status);
        Assert.DoesNotContain(ArrFixtures.RadarrKey, outcome.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("radarr:7878", outcome.Detail, StringComparison.Ordinal);
    }
}
