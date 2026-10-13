using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tmdb;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class TmdbMapperTests
{
    [Fact]
    public void ATitle_KeepsItsGenres_SoTheClientCanLeaveSomeOut()
    {
        var entry = new TmdbModels.Entry { Id = 99, Title = "Food, Inc.", MediaType = "movie", GenreIds = [99, 36] };

        var item = TmdbMapper.ToItem(entry);

        Assert.NotNull(item);
        Assert.Equal([99, 36], item.GenreIds);
    }

    [Fact]
    public void APerson_HasNoGenre()
    {
        var entry = new TmdbModels.Entry { Id = 287, Name = "Brad Pitt", MediaType = "person", GenreIds = [18] };

        var item = TmdbMapper.ToItem(entry);

        Assert.NotNull(item);
        Assert.Equal(CatalogItemKind.Person, item.Kind);
        Assert.Empty(item.GenreIds);
    }
}
