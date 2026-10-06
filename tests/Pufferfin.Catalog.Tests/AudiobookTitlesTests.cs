using Pufferfin.Catalog.Audible;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class AudiobookTitlesTests
{
    [Theory]
    [InlineData("Harry Potter - L'Integrale", null, null, true)]
    [InlineData("Harry Potter", "The Complete Collection", null, true)]
    [InlineData("Harry Potter", null, "1-7", true)]
    [InlineData("Harry Potter et l'Ordre du Phenix", null, "5", false)]
    [InlineData("Dune", "Version integrale", "1", false)]
    public void AnAudibleEntry_IsACompleteEdition_ByItsTitleSubtitleOrPosition(string title, string? subtitle, string? position, bool expected)
        => Assert.Equal(expected, AudiobookTitles.IsCompleteEdition(title, subtitle, [position]));

    [Theory]
    [InlineData("Harry Potter - L'Integrale", "harry potter")]
    [InlineData("Harry Potter, 1-7", "harry potter")]
    [InlineData("The Wheel of Time: The Complete Series", "wheel of time")]
    [InlineData("Le cycle de Dune, tomes 1 a 6", "le cycle de dune")]
    public void TheCoreOfACompleteEdition_IsItsSeries(string title, string core)
        => Assert.Equal(core, string.Join(' ', AudiobookTitles.CoreWords(title)));
}
