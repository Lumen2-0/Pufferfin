using System;
using Pufferfin.Server.Configuration;
using Xunit;

namespace Pufferfin.Server.Tests.Configuration;

public sealed class SettingValueParserTests
{
    [Theory]
    [InlineData("http://radarr:7878", "http://radarr:7878")]
    [InlineData("http://radarr:7878/", "http://radarr:7878")]
    [InlineData(" https://example.org/radarr/ ", "https://example.org/radarr")]
    public void Url_IsNormalized(string raw, string expected)
    {
        Assert.True(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.RadarrUrl), raw, out var normalized, out _));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("radarr:7878")]
    [InlineData("ftp://radarr")]
    [InlineData("http://radarr:7878/?apikey=hunter2")]
    [InlineData("http://admin:hunter2@radarr:7878")]
    public void InvalidUrl_IsRejected_WithoutEchoingTheValue(string raw)
    {
        Assert.False(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.RadarrUrl), raw, out _, out var error));
        Assert.NotNull(error);
        Assert.DoesNotContain("hunter2", error, StringComparison.Ordinal);
        Assert.DoesNotContain(raw.Trim(), error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fr,en", "fr,en")]
    [InlineData(" fr , pt-BR ,, fr ", "fr,pt-BR")]
    public void LanguageList_IsNormalized(string raw, string expected)
    {
        Assert.True(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.PreferredAudioLanguages), raw, out var normalized, out _));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("french")]
    [InlineData("fr;en")]
    public void InvalidLanguage_IsRejected(string raw)
        => Assert.False(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.PreferredAudioLanguages), raw, out _, out _));

    [Theory]
    [InlineData("true", "true")]
    [InlineData("1", "true")]
    [InlineData("No", "false")]
    public void Boolean_IsNormalized(string raw, string expected)
    {
        Assert.True(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.RejectSubtitledOnly), raw, out var normalized, out _));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(SettingKeys.MaxConcurrentDownloads, "0")]
    [InlineData(SettingKeys.SeedMinDays, "-1")]
    [InlineData(SettingKeys.SeedMinDays, "1.5")]
    [InlineData(SettingKeys.SeedTargetRatio, "-0.1")]
    public void NumberOutOfRange_IsRejected(string key, string raw)
        => Assert.False(SettingValueParser.TryParse(SettingKeys.Get(key), raw, out _, out _));

    [Fact]
    public void Secret_IsKeptVerbatim_ExceptSurroundingWhiteSpace()
    {
        Assert.True(SettingValueParser.TryParse(SettingKeys.Get(SettingKeys.RadarrApiKey), " a,b c ", out var normalized, out _));
        Assert.Equal("a,b c", normalized);
    }
}
