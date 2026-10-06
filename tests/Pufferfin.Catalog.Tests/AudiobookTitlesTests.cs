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
    [InlineData("Le Trone de fer - L'Integrale 2", 2, null, null, null)]
    [InlineData("Le Trone de fer - L'Integrale tome 1 [FR]", 1, null, null, null)]
    [InlineData("Harry Potter 1-7", null, 1, 7, null)]
    [InlineData("Harry Potter Books 1-3 [FR]", null, 1, 3, null)]
    [InlineData("Dune T01-T06", null, 1, 6, null)]
    [InlineData("Harry Potter L'Integrale 1-7", null, 1, 7, null)]
    [InlineData("Dune, l'Integrale (6 tomes)", null, null, null, 6)]
    [InlineData("Harry Potter - L'Integrale", null, null, null, null)]
    public void TheEditionOfACompleteEdition_IsRead(string title, int? number, int? from, int? to, int? count)
    {
        var edition = AudiobookTitles.EditionOf(title);

        Assert.Equal(number, edition.Number);
        Assert.Equal(from is null ? null : (from.Value, to!.Value), edition.Range);
        Assert.Equal(count, edition.Count);
    }

    [Theory]
    [InlineData("Harry Potter - L'Integrale", "harry potter")]
    [InlineData("Harry Potter, 1-7", "harry potter")]
    [InlineData("The Wheel of Time: The Complete Series", "wheel of time")]
    [InlineData("Le cycle de Dune, tomes 1 a 6", "le cycle de dune")]
    public void TheCoreOfACompleteEdition_IsItsSeries(string title, string core)
        => Assert.Equal(core, string.Join(' ', AudiobookTitles.CoreWords(title)));
}
