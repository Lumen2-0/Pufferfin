using Pufferfin.Catalog.Tmdb;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public class FilmographyTests
{
    [Theory]
    [InlineData("Himself", null, true)]
    [InlineData("Self - Guest", null, true)]
    [InlineData("Self", null, true)]
    [InlineData(null, 10767, true)]
    [InlineData(null, 10764, true)]
    [InlineData("Selina Kyle", null, false)]
    [InlineData("Bruce Wayne", 28, false)]
    public void AnAppearanceAsOneself_IsNotAWork(string? character, int? genre, bool isAppearance)
    {
        var credit = new TmdbModels.Entry { Character = character };
        if (genre is int id)
        {
            credit.GenreIds.Add(id);
        }

        Assert.Equal(isAppearance, TmdbCatalogService.IsAppearance(credit));
    }
}
