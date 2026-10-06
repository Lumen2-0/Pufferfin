using System.Linq;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Storage;
using Pufferfin.Server.Tests.AutoConfig;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// Every setting of the seeding policy and of the storage screen lives in the store, editable from the interface.
/// </summary>
public sealed class StorageSettingsTests
{
    [Theory]
    [InlineData("/media", "/media")]
    [InlineData("/data/media/", "/data/media")]
    [InlineData("/", "/")]
    public void Path_IsAbsolute_WithoutTrailingSeparator(string raw, string expected)
    {
        Assert.True(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.MediaPath), raw, out var normalized, out var error), error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("media")]
    [InlineData("./media")]
    [InlineData("/data/../etc")]
    public void Path_RelativeOrClimbing_IsRejected(string raw)
    {
        Assert.False(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.DownloadsPath), raw, out _, out var error));
        Assert.Contains(SettingKeys.DownloadsPath, error, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("100", true)]
    [InlineData("101", false)]
    public void StorageWarningPercent_IsBetweenOneAndAHundred(string raw, bool valid)
    {
        Assert.Equal(valid, SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.StorageWarningPercent), raw, out _, out _));
    }

    [Fact]
    public void EverySeedingAndStorageSetting_IsEditableInTheStore()
    {
        using var context = new StoreTestContext();
        var store = context.Start();

        var settings = store.Update(StoreTestContext.Changes(
            (SettingKeys.SeedMinDays, 10),
            (SettingKeys.SeedMaxDays, 40),
            (SettingKeys.SeedTargetRatio, "2.5"),
            (SettingKeys.SeedCategories, new[] { "radarr", "books" }),
            (SettingKeys.MediaPath, "/data/media"),
            (SettingKeys.DownloadsPath, "/data/downloads"),
            (SettingKeys.QBittorrentDownloadsPath, "/downloads"),
            (SettingKeys.StorageWarningPercent, 85))).Settings;

        Assert.Equal(10, settings.SeedMinDays);
        Assert.Equal(new[] { "radarr", "books" }, settings.SeedCategories);
        var paths = StoragePaths.From(settings);
        Assert.Equal(new StoragePaths("/data/media", "/data/downloads", "/downloads", 85), paths);
        Assert.DoesNotContain(SettingKeys.All.Where(d => d.Key is SettingKeys.MediaPath or SettingKeys.DownloadsPath or SettingKeys.StorageWarningPercent or SettingKeys.SeedCategories), d => d.IsSecret);
    }

    [Fact]
    public void StoragePaths_HaveSafeDefaults()
    {
        var paths = StoragePaths.From(ArrFixtures.Settings());

        Assert.Equal(new StoragePaths("/media", "/downloads", "/downloads", 90), paths);
    }

    [Theory]
    [InlineData("/downloads/radarr/Film.mkv", "/data/downloads/radarr/Film.mkv")]
    [InlineData("/downloads", "/data/downloads")]
    [InlineData("/downloadsX/Film.mkv", "/downloadsX/Film.mkv")]
    [InlineData("/elsewhere/Film.mkv", "/elsewhere/Film.mkv")]
    public void StoragePaths_TranslateWhatQBittorrentSees(string qBittorrentPath, string expected)
    {
        var paths = new StoragePaths("/data/media", "/data/downloads", "/downloads", 90);

        Assert.Equal(expected, paths.ToLocalPath(qBittorrentPath).Replace('\\', '/'));
    }
}
