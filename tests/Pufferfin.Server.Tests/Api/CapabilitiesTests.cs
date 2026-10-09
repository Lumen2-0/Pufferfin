using Pufferfin.Server.Api;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Api;

public sealed class CapabilitiesTests
{
    [Fact]
    public void UnconfiguredServices_AreReportedDisabled_NotInError()
    {
        using var context = new StoreTestContext();
        var controller = new PufferfinCapabilitiesController(context.Start());

        var capabilities = controller.GetPufferfinCapabilities().Value!;

        Assert.False(capabilities.Radarr);
        Assert.False(capabilities.Sonarr);
        Assert.False(capabilities.Prowlarr);
        Assert.False(capabilities.QBittorrent);
        Assert.False(capabilities.Bazarr);
        Assert.False(capabilities.Shelfmark);
        Assert.False(capabilities.Tmdb);
        Assert.False(capabilities.FlareSolverr);
    }

    [Fact]
    public void Capabilities_FollowASave_WithoutRestart()
    {
        using var context = new StoreTestContext();
        var store = context.Start();
        var controller = new PufferfinCapabilitiesController(store);

        store.Update(StoreTestContext.Changes((SettingKeys.RadarrUrl, "http://radarr:7878"), (SettingKeys.RadarrApiKey, "key")));
        Assert.True(controller.GetPufferfinCapabilities().Value!.Radarr);

        store.Update(StoreTestContext.Changes((SettingKeys.RadarrApiKey, null)));
        Assert.False(controller.GetPufferfinCapabilities().Value!.Radarr);
    }
}
