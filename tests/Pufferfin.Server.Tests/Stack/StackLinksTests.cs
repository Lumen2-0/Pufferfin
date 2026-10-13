using System.Linq;
using Pufferfin.Server.Stack;
using Xunit;

namespace Pufferfin.Server.Tests.Stack;

public class StackLinksTests
{
    [Fact]
    public void NoVariable_GivesNoLink()
    {
        Assert.Empty(StackLinks.Build(null, null));
        Assert.Empty(StackLinks.Build("  ", string.Empty));
    }

    [Fact]
    public void Domain_GivesEachStandardServiceOnItsSubDomain()
    {
        var links = StackLinks.Build("admin.example.com", null);

        Assert.Equal(new[] { "Radarr", "Sonarr", "Prowlarr", "Bazarr", "qBittorrent", "Shelfmark" }, links.Select(link => link.Name));
        Assert.Equal("https://radarr.admin.example.com", links[0].Url);
        Assert.Equal("https://qbittorrent.admin.example.com", links[4].Url);
    }

    [Theory]
    [InlineData("https://admin.example.com/")]
    [InlineData(" admin.example.com/ ")]
    public void Domain_WithSchemeOrSlash_IsCleaned(string domain)
    {
        Assert.Equal("https://sonarr.admin.example.com", StackLinks.Build(domain, null)[1].Url);
    }

    [Fact]
    public void InvalidDomain_IsIgnored()
    {
        Assert.Empty(StackLinks.Build("not a domain!", null));
    }

    [Fact]
    public void Links_ReplaceAServiceOfTheSameName_AndAddTheOthers()
    {
        var links = StackLinks.Build("admin.example.com", "radarr=https://movies.example.com; Jellyseerr = https://requests.example.com/ ");

        Assert.Equal(7, links.Count);
        Assert.Equal("radarr", links[0].Name);
        Assert.Equal("https://movies.example.com", links[0].Url);
        Assert.Equal("Jellyseerr", links[6].Name);
        Assert.Equal("https://requests.example.com/", links[6].Url);
    }

    [Fact]
    public void Links_Alone_GiveOnlyThemselves()
    {
        var links = StackLinks.Build(null, "Radarr=http://10.0.0.2:7878");

        Assert.Single(links);
        Assert.Equal("http://10.0.0.2:7878", links[0].Url);
    }

    [Theory]
    [InlineData("Radarr")]
    [InlineData("=https://x.example.com")]
    [InlineData("Radarr=javascript:alert(1)")]
    [InlineData("Radarr=ftp://x.example.com")]
    [InlineData("Radarr=/relative")]
    public void MalformedEntries_AreIgnored(string entry)
    {
        Assert.Empty(StackLinks.Build(null, entry));
    }
}
