using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Configuration;

public sealed class ConfigurationStoreTests
{
    [Fact]
    public void Configuration_SurvivesARestart()
    {
        using var context = new StoreTestContext();

        context.Start().Update(StoreTestContext.Changes(
            (SettingKeys.SonarrUrl, "http://sonarr:8989/"),
            (SettingKeys.SonarrApiKey, "sonarr-secret-key"),
            (SettingKeys.PreferredAudioLanguages, new[] { "fr", "en" }),
            (SettingKeys.SeedMinDays, 7),
            (SettingKeys.SeedTargetRatio, "1.5"),
            (SettingKeys.RejectSubtitledOnly, true)));

        var settings = context.Start().Current.Settings;

        Assert.True(settings.IsEnabled(PufferfinService.Sonarr));
        Assert.Equal(new Uri("http://sonarr:8989"), settings.GetService(PufferfinService.Sonarr)!.BaseUrl);
        Assert.Equal("sonarr-secret-key", settings.GetService(PufferfinService.Sonarr)!.ApiKey);
        Assert.Equal(["fr", "en"], settings.PreferredAudioLanguages);
        Assert.Equal(7, settings.SeedMinDays);
        Assert.Equal(1.5m, settings.SeedTargetRatio);
        Assert.True(settings.RejectSubtitledOnly);
    }

    [Fact]
    public void AbsentKey_IsLeftUnchanged_AndNullClears()
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        store.Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, "http://radarr:7878"), (SettingKeys.RadarrApiKey, "radarr-key")));

        store.Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, "http://radarr:7979")));
        Assert.Equal("radarr-key", store.Current.Settings.GetService(PufferfinService.Radarr)!.ApiKey);

        store.Update(StoreTestContext.Changes((SettingKeys.RadarrApiKey, null)));
        Assert.False(store.Current.StatesByKey[SettingKeys.RadarrApiKey].IsSet);
        Assert.False(store.Current.Settings.IsEnabled(PufferfinService.Radarr));
    }

    [Fact]
    public void BlankSecret_IsRejected_SoAnEmptyFieldNeverErasesAKey()
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        store.Update(StoreTestContext.Changes((SettingKeys.RadarrApiKey, "radarr-key")));

        Assert.Throws<SettingsValidationException>(() => store.Update(StoreTestContext.Changes((SettingKeys.RadarrApiKey, "  "))));
        Assert.True(store.Current.StatesByKey[SettingKeys.RadarrApiKey].IsSet);
    }

    [Fact]
    public void UnknownKey_IsRejected()
    {
        using var context = new StoreTestContext();

        var exception = Assert.Throws<SettingsValidationException>(() => context.Start().Update(StoreTestContext.Changes(("RADARR_URLL", "http://x"))));

        Assert.Contains("RADARR_URLL", exception.Errors.Keys);
    }

    [Fact]
    public void SeedMaxDays_BelowSeedMinDays_IsRejected()
    {
        using var context = new StoreTestContext();

        var exception = Assert.Throws<SettingsValidationException>(
            () => context.Start().Update(StoreTestContext.Changes((SettingKeys.SeedMinDays, 10), (SettingKeys.SeedMaxDays, 5))));

        Assert.Contains(SettingKeys.SeedMaxDays, exception.Errors.Keys);
    }

    [Fact]
    public void UnreadableFile_IsMovedAside_AndTheStoreStartsEmpty()
    {
        using var context = new StoreTestContext();
        File.WriteAllText(context.FilePath, "{ not json");

        var store = context.Start();

        Assert.All(store.Current.States, s => Assert.Equal(SettingSource.Unset, s.Source));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(context.FilePath)!, "pufferfin.json.invalid-*"));
    }

    [Fact]
    public void UnknownKeysInTheFile_ArePreserved()
    {
        using var context = new StoreTestContext();
        File.WriteAllText(context.FilePath, """{ "Version": 1, "Settings": { "FUTURE_KEY": { "Value": "x", "FromEnvironment": false } } }""");

        context.Start().Update(StoreTestContext.Changes((SettingKeys.SeedMinDays, 3)));

        var document = JsonNode.Parse(File.ReadAllText(context.FilePath))!;
        Assert.Equal("x", (string?)document["Settings"]!["FUTURE_KEY"]!["Value"]);
    }

    [Fact]
    public void InvalidStoredValue_IsIgnored()
    {
        using var context = new StoreTestContext();
        File.WriteAllText(context.FilePath, """{ "Version": 1, "Settings": { "SEED_MIN_DAYS": { "Value": "abc", "FromEnvironment": false } } }""");

        var store = context.Start();

        Assert.Null(store.Current.Settings.SeedMinDays);
    }

    [Fact]
    public void Saved_IsRaisedOnEverySave()
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        var count = 0;
        store.Saved += (_, _) => count++;

        store.Update(StoreTestContext.Changes((SettingKeys.SeedMinDays, 3)));
        store.Update(StoreTestContext.Changes((SettingKeys.SeedMinDays, 3)));

        Assert.Equal(2, count);
    }

    [Fact]
    public void ServiceWithUrlButNoKey_IsDisabled_NotInError()
    {
        using var context = new StoreTestContext();
        var store = context.Start();

        store.Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, "http://radarr:7878")));

        Assert.False(store.Current.Settings.IsEnabled(PufferfinService.Radarr));
        Assert.Null(store.Current.Settings.GetService(PufferfinService.Radarr));
    }

    [Fact]
    public void EmptyStore_EveryServiceIsDisabled()
    {
        using var context = new StoreTestContext();

        var settings = context.Start().Current.Settings;

        Assert.All(Enum.GetValues<PufferfinService>(), s => Assert.False(settings.IsEnabled(s)));
        Assert.Empty(settings.PreferredAudioLanguages);
        Assert.Null(settings.MaxConcurrentDownloads);
        Assert.Equal(SettingKeys.All.Count, context.Start().Current.States.Count);
        Assert.Equal(SettingKeys.All.Select(d => d.Key), context.Start().Current.States.Select(s => s.Definition.Key));
    }
}
