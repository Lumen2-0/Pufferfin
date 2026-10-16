using System;
using System.Collections.Generic;
using System.Linq;

namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// A brand Discover filters by: a streaming service, a studio or a network, with its TMDB ids.
/// </summary>
/// <param name="Key">The key of the brand in addresses (<c>netflix</c>).</param>
/// <param name="Name">The name shown.</param>
/// <param name="Kind">What it filters by.</param>
/// <param name="Ids">The TMDB ids, any of them matching (a service has a different id in some countries).</param>
/// <param name="LogoPath">The TMDB path of its logo: the app icon of a service, the wordmark of a studio or a network.</param>
/// <param name="Region">For a service offered in one country only, that country; otherwise the configured one.</param>
public sealed record DiscoverBrand(string Key, string Name, DiscoverBrandKind Kind, IReadOnlyList<int> Ids, string LogoPath, string? Region = null)
{
    /// <summary>
    /// Gets the brands of the Discover screen, in their order on screen.
    /// </summary>
    public static IReadOnlyList<DiscoverBrand> All { get; } =
    [
        new("apple", "Apple TV+", DiscoverBrandKind.Service, [350], "/9icYBfYFcwgCbky5VdGUIKJ4C5i.png"),
        new("netflix", "Netflix", DiscoverBrandKind.Service, [8], "/rK1KljqmbvO9HQa1PBFLILWah72.png"),
        new("prime", "Prime Video", DiscoverBrandKind.Service, [119, 9], "/gMZdpavHmxFNnLpMHwVxfqeux2g.png"),
        new("disney", "Disney+", DiscoverBrandKind.Service, [337], "/5eZ872CghnHFLB1j8grszbrx0dx.png"),
        new("paramount", "Paramount+", DiscoverBrandKind.Service, [531], "/pkx3klJlwW5JdtaulvDx6hDNtch.png"),
        new("canal", "Canal+", DiscoverBrandKind.Service, [381], "/gliWKhLJVdm7QwL1braBCRko5dx.png"),
        new("warner", "Warner Bros. Pictures", DiscoverBrandKind.Studio, [174, 1957], "/zhD3hhtKB5qyv7ZeL4uLpNxgMVU.png"),
        new("dc", "DC", DiscoverBrandKind.Studio, [429, 9993, 184898], "/4Y00XuSMuP1gimd0jP6JT57QbCI.png"),
        new("marvel", "Marvel Studios", DiscoverBrandKind.Studio, [420], "/hUzeosd33nzE5MCNsZxCGEKTXaQ.png"),
        new("adultswim", "Adult Swim", DiscoverBrandKind.Network, [80], "/tHZPHOLc6iF27G34cAZGPsMtMSy.png")
    ];

    /// <summary>
    /// Finds a brand by key.
    /// </summary>
    /// <param name="key">The key, any case.</param>
    /// <returns>The brand, or <c>null</c>.</returns>
    public static DiscoverBrand? Find(string? key)
        => string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(b => b.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));
}
