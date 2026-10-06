using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Server.Catalog;
using Xunit;

namespace Pufferfin.Server.Tests.Catalog;

public sealed class CatalogRegistrationTests
{
    [Fact]
    public void AddPufferfin_WiresTheCatalogToTheStores_NotToTheEnvironment()
    {
        var services = new ServiceCollection().AddPufferfin();

        Assert.Equal(typeof(StoreCatalogSettingsProvider), Assert.Single(services, d => d.ServiceType == typeof(ICatalogSettingsProvider)).ImplementationType);
        Assert.Equal(typeof(StoreLetterboxdAccountProvider), Assert.Single(services, d => d.ServiceType == typeof(ILetterboxdAccountProvider)).ImplementationType);
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(EnvironmentCatalogSettingsProvider));
    }
}
