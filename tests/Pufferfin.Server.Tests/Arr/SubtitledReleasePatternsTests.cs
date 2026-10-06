using System.Text.RegularExpressions;
using Pufferfin.Server.Arr;
using Xunit;

namespace Pufferfin.Server.Tests.Arr;

public sealed class SubtitledReleasePatternsTests
{
    [Theory]
    [InlineData("Dune.Part.Two.2024.VOSTFR.1080p.WEB.x264-GRP")]
    [InlineData("Dune Part Two 2024 VOST 1080p")]
    [InlineData("Dune.Part.Two.2024.SUBFRENCH.1080p.BluRay")]
    [InlineData("Dune.Part.Two.2024.FRENCH.SUBS.1080p")]
    [InlineData("Dune.Part.Two.2024.STFR.2160p")]
    [InlineData("Shogun.2024.S01E03.Subbed.1080p")]
    public void French_SubtitledOnlyReleases_AreRecognized(string title)
    {
        var patterns = SubtitledReleasePatterns.For("fr")!;

        Assert.True(patterns.IsSubtitledOnly(title));
    }

    [Theory]
    [InlineData("Dune.Part.Two.2024.MULTi.VFF.1080p.WEB.x264-GRP")]
    [InlineData("Dune.Part.Two.2024.TRUEFRENCH.1080p.BluRay")]
    [InlineData("Dune.Part.Two.2024.FRENCH.1080p.WEB")]
    [InlineData("Dune.Part.Two.2024.MULTI.1080p.VOSTFR")]
    [InlineData("Dune.Part.Two.2024.1080p.BluRay.x264-GRP")]
    public void French_AudioReleases_AreNotSubtitledOnly(string title)
    {
        var patterns = SubtitledReleasePatterns.For("fr")!;

        Assert.False(patterns.IsSubtitledOnly(title));
    }

    [Theory]
    [InlineData("de", "Film.2024.German.Subbed.1080p", true)]
    [InlineData("de", "Film.2024.German.DL.1080p", false)]
    [InlineData("de", "Film.2024.GERMAN.1080p", false)]
    [InlineData("en", "Anime.S01E01.English.Subbed.1080p", true)]
    [InlineData("en", "Anime.S01E01.English.Dubbed.1080p", false)]
    [InlineData("it", "Film.2024.SUB.ITA.1080p", true)]
    [InlineData("it", "Film.2024.ITA.ENG.1080p", false)]
    [InlineData("es", "Pelicula.2024.VOSE.1080p", true)]
    public void OtherLanguages_FollowTheSameRule(string language, string title, bool subtitledOnly)
    {
        var patterns = SubtitledReleasePatterns.For(language)!;

        Assert.Equal(subtitledOnly, patterns.IsSubtitledOnly(title));
    }

    [Fact]
    public void ThePatterns_AreValidRegularExpressions_ForTheArrCustomFormats()
    {
        foreach (var language in new[] { "fr", "en", "de", "es", "it", "ja", "pt-BR", "es-419" })
        {
            var patterns = SubtitledReleasePatterns.For(language)!;

            _ = new Regex(patterns.SubtitledPattern, RegexOptions.IgnoreCase);
            _ = new Regex(patterns.AudioPattern, RegexOptions.IgnoreCase);
        }
    }

    [Fact]
    public void AnUnknownLanguage_HasNoPatterns()
        => Assert.Null(SubtitledReleasePatterns.For("xx"));
}
