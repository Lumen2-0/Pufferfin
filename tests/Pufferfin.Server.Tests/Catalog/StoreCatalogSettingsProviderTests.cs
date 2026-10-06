using System.Collections.Generic;
using Pufferfin.Server.Catalog;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Catalog;

/// <summary>
/// The catalog reads the configuration store, never the environment directly.
/// </summary>
public sealed class StoreCatalogSettingsProviderTests
{
    private const string EnvironmentKey = "env0000000000000000000000000000a";
    private const string UiKey = "ui00000000000000000000000000000b";

    [Fact]
    public void KeyEnteredInTheAdminPanel_EnablesTmdb_WithoutAnyEnvironmentVariable()
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        var provider = new StoreCatalogSettingsProvider(store);
        Assert.False(provider.GetSettings().IsTmdbEnabled);

        store.Update(StoreTestContext.Changes(
            (SettingKeys.TmdbApiKey, UiKey),
            (SettingKeys.TmdbLanguages, new[] { "fr-FR", "en" }),
            (SettingKeys.AudibleMarkets, new[] { "fr", "us" })));

        // Read on every call: the change applies without a restart.
        var settings = provider.GetSettings();
        Assert.True(settings.IsTmdbEnabled);
        Assert.Equal(UiKey, settings.TmdbApiKey);
        Assert.Equal(["fr-FR", "en"], settings.TmdbLanguages);
        Assert.Equal(["fr", "us"], settings.AudibleMarkets);
    }

    [Fact]
    public void KeyReplacedInTheAdminPanel_WinsOverTheEnvironmentThatSeededIt_AfterARestart()
    {
        using var context = new StoreTestContext();
        var seeded = new Dictionary<string, string> { [SettingKeys.TmdbApiKey] = EnvironmentKey, [SettingKeys.TmdbLanguages] = "de" };

        // Seeded by the environment, then the variable is dropped and the admin replaces the key in the UI.
        Assert.Equal(EnvironmentKey, new StoreCatalogSettingsProvider(context.Start(seeded)).GetSettings().TmdbApiKey);
        context.Start().Update(StoreTestContext.Changes((SettingKeys.TmdbApiKey, UiKey), (SettingKeys.TmdbLanguages, new[] { "fr" })));

        // Redeployed with the old variables still present.
        var settings = new StoreCatalogSettingsProvider(context.Start(seeded)).GetSettings();

        Assert.Equal(UiKey, settings.TmdbApiKey);
        Assert.Equal(["fr"], settings.TmdbLanguages);
    }

    [Fact]
    public void UnsetLanguages_FallBackToTheCatalogDefault()
    {
        using var context = new StoreTestContext();

        var settings = new StoreCatalogSettingsProvider(context.Start()).GetSettings();

        Assert.Equal([global::Pufferfin.Catalog.Configuration.CatalogSettings.DefaultTmdbLanguage], settings.TmdbLanguages);
        Assert.Empty(settings.AudibleMarkets);
    }
}
