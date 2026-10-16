using System;
using System.Globalization;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Tmdb;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public class DiscoverFiltersTests
{
    [Fact]
    public void Popular_SortsByPopularity_WithTheUsualVoteFloor()
    {
        var filters = TmdbClient.DiscoverFilters(true, new TmdbDiscoverOptions());

        Assert.Contains("&sort_by=popularity.desc", filters, StringComparison.Ordinal);
        Assert.Contains("&vote_count.gte=50", filters, StringComparison.Ordinal);
    }

    [Fact]
    public void TopRated_OfAYear_UsesALowerFloorThanOfAllTime()
    {
        var ofAllTime = TmdbClient.DiscoverFilters(true, new TmdbDiscoverOptions { Sort = TmdbDiscoverSort.TopRated });
        var ofAYear = TmdbClient.DiscoverFilters(true, new TmdbDiscoverOptions { Sort = TmdbDiscoverSort.TopRated, Year = 2026 });

        Assert.Contains("&sort_by=vote_average.desc&vote_count.gte=1000", ofAllTime, StringComparison.Ordinal);
        Assert.Contains("&vote_count.gte=150", ofAYear, StringComparison.Ordinal);
        Assert.Contains("&primary_release_year=2026", ofAYear, StringComparison.Ordinal);
    }

    [Fact]
    public void Newest_Series_KeepsOnlyWhatAiredByToday()
    {
        var filters = TmdbClient.DiscoverFilters(false, new TmdbDiscoverOptions { Sort = TmdbDiscoverSort.Newest });

        Assert.Contains("&sort_by=first_air_date.desc", filters, StringComparison.Ordinal);
        Assert.Contains("&first_air_date.lte=" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), filters, StringComparison.Ordinal);
    }

    [Fact]
    public void Keywords_ArePassedAsAnyOf()
    {
        var filters = TmdbClient.DiscoverFilters(true, new TmdbDiscoverOptions { Keywords = "207317,1991" });

        Assert.Contains("&with_keywords=207317|1991", filters, StringComparison.Ordinal);
    }

    [Fact]
    public void OriginalLanguage_IsKeptInLowerCase()
    {
        var filters = TmdbClient.DiscoverFilters(false, new TmdbDiscoverOptions { OriginalLanguage = "KO" });

        Assert.Contains("&with_original_language=ko", filters, StringComparison.Ordinal);
    }

    [Fact]
    public void AService_KeepsTheSubscriptionOffersOfTheRegion()
    {
        var filters = TmdbClient.DiscoverFilters(true, new TmdbDiscoverOptions { Brand = DiscoverBrand.Find("netflix") }, "FR");

        Assert.Contains("&with_watch_providers=8&watch_region=FR&with_watch_monetization_types=flatrate", filters, StringComparison.Ordinal);
    }

    [Fact]
    public void AServiceOfSeveralIds_KeepsAnyOfThem()
    {
        var prime = TmdbClient.DiscoverFilters(true, new TmdbDiscoverOptions { Brand = DiscoverBrand.Find("prime") }, "FR");

        Assert.Contains("&with_watch_providers=119%7C9&watch_region=FR", prime, StringComparison.Ordinal);
    }

    [Fact]
    public void AStudio_AndANetwork_FilterByCompaniesAndNetworks()
    {
        var marvel = TmdbClient.DiscoverFilters(true, new TmdbDiscoverOptions { Brand = DiscoverBrand.Find("marvel") }, "FR");
        var adultSwim = TmdbClient.DiscoverFilters(false, new TmdbDiscoverOptions { Brand = DiscoverBrand.Find("adultswim") }, "FR");

        Assert.Contains("&with_companies=420", marvel, StringComparison.Ordinal);
        Assert.DoesNotContain("watch_region", marvel, StringComparison.Ordinal);
        Assert.Contains("&with_networks=80", adultSwim, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownBrand_IsNotFound()
    {
        Assert.Null(DiscoverBrand.Find("hbo"));
        Assert.Null(DiscoverBrand.Find(null));
    }

    [Theory]
    [InlineData("fr-FR", "FR")]
    [InlineData("en-US", "US")]
    [InlineData("fr", "US")]
    public void WatchRegion_IsTheCountryOfTheFirstLanguage(string language, string region)
    {
        Assert.Equal(region, new Pufferfin.Catalog.Configuration.CatalogSettings { TmdbLanguages = [language] }.WatchRegion);
    }

    [Theory]
    [InlineData("fr", "horreur")]
    [InlineData("de", "horror")]
    [InlineData("us", "horror")]
    [InlineData("FR", "horreur")]
    public void AudibleThemes_UseTheLanguageOfTheMarket(string market, string expected)
    {
        Assert.Equal(expected, AudibleThemes.Keywords(AudibleTheme.Horror, market));
    }
}
