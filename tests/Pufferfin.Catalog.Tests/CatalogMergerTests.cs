using System;
using System.Linq;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Search;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public class CatalogMergerTests
{
    private static readonly Guid _localMatrixId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CatalogItem TmdbMovie(int id, string title, int year, string? originalTitle = null)
        => new()
        {
            Key = "tmdb:movie:" + id,
            Kind = CatalogItemKind.Movie,
            Title = title,
            OriginalTitle = originalTitle,
            Year = year,
            TmdbId = id,
            Availability = CatalogAvailability.Missing,
            Sources = [CatalogSource.Tmdb]
        };

    [Fact]
    public void Merge_TitleOnDiskAndOnTmdb_ProducesOneEntryMarkedOnDisk()
    {
        var local = new LocalCatalogEntry { ItemId = _localMatrixId, Kind = CatalogItemKind.Movie, Title = "The Matrix", Year = 1999, TmdbId = 603 };
        var tmdb = new[] { TmdbMovie(603, "Matrix", 1999, "The Matrix"), TmdbMovie(604, "Matrix Reloaded", 2003) };

        var merged = CatalogMerger.Merge([local], tmdb, [], 20);

        Assert.Equal(2, merged.Count);
        var matrix = Assert.Single(merged, i => i.TmdbId == 603);
        Assert.Equal(CatalogAvailability.OnDisk, matrix.Availability);
        Assert.Equal(_localMatrixId, matrix.LocalItemId);
        Assert.Equal("tmdb:movie:603", matrix.Key);
        Assert.Equal([CatalogSource.Library, CatalogSource.Tmdb], matrix.Sources);
        Assert.Equal(CatalogAvailability.Missing, Assert.Single(merged, i => i.TmdbId == 604).Availability);
        Assert.Equal(merged.Count, merged.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void Merge_LocalItemWithoutTmdbId_MatchesByNormalizedTitleAndYear()
    {
        var local = new LocalCatalogEntry { ItemId = _localMatrixId, Kind = CatalogItemKind.Movie, Title = "Amélie", Year = 2001 };
        var tmdb = new[] { TmdbMovie(194, "Amelie from Montmartre", 2001, "AMELIE") };

        var merged = CatalogMerger.Merge([local], tmdb, [], 20);

        var single = Assert.Single(merged);
        Assert.Equal(CatalogAvailability.OnDisk, single.Availability);
        Assert.Equal(194, single.TmdbId);
    }

    [Fact]
    public void Merge_SameTitleDifferentYear_StaysTwoEntries()
    {
        var local = new LocalCatalogEntry { ItemId = _localMatrixId, Kind = CatalogItemKind.Movie, Title = "Dune", Year = 1984 };
        var tmdb = new[] { TmdbMovie(438631, "Dune", 2021) };

        var merged = CatalogMerger.Merge([local], tmdb, [], 20);

        Assert.Equal(2, merged.Count);
        Assert.Equal(CatalogAvailability.Missing, Assert.Single(merged, i => i.TmdbId == 438631).Availability);
    }

    [Fact]
    public void Merge_LocalItemWithAnotherTmdbId_IsNeverMergedByTitle()
    {
        var local = new LocalCatalogEntry { ItemId = _localMatrixId, Kind = CatalogItemKind.Movie, Title = "Dune", Year = 2021, TmdbId = 841 };
        var tmdb = new[] { TmdbMovie(438631, "Dune", 2021) };

        var merged = CatalogMerger.Merge([local], tmdb, [], 20);

        Assert.Equal(2, merged.Count);
        Assert.Equal("tmdb:movie:841", Assert.Single(merged, i => i.Availability == CatalogAvailability.OnDisk).Key);
    }

    [Fact]
    public void Merge_SeriesAndMovieWithSameTmdbId_AreDifferentEntries()
    {
        var local = new LocalCatalogEntry { ItemId = _localMatrixId, Kind = CatalogItemKind.Series, Title = "Something", TmdbId = 603 };
        var tmdb = new[] { TmdbMovie(603, "Matrix", 1999) };

        var merged = CatalogMerger.Merge([local], tmdb, [], 20);

        Assert.Equal(2, merged.Count);
        Assert.Equal(CatalogAvailability.Missing, Assert.Single(merged, i => i.Kind == CatalogItemKind.Movie).Availability);
    }

    [Fact]
    public void Merge_AudiobookOnDisk_MatchesAudibleByAsinThenByTitleAndAuthor()
    {
        var byAsin = new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Audiobook, Title = "Dune (Unabridged)", Asin = "b0fr4nc3d1" };
        var byTitle = new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Audiobook, Title = "Foundation", Authors = ["Isaac Asimov"] };
        CatalogItem Book(string asin, string title, string author) => new()
        {
            Key = "audible:" + asin,
            Kind = CatalogItemKind.Audiobook,
            Title = title,
            Asin = asin,
            Authors = [author],
            Availability = CatalogAvailability.Missing,
            Sources = [CatalogSource.Audible]
        };

        var merged = CatalogMerger.Merge(
            [byAsin, byTitle],
            [],
            [Book("B0FR4NC3D1", "Dune", "Frank Herbert"), Book("B0F0NDAT10", "Foundation", "Isaac Asimov"), Book("B0OTHER000", "Foundation", "Someone Else")],
            20);

        Assert.Equal(3, merged.Count);
        Assert.Equal(byAsin.ItemId, Assert.Single(merged, i => i.Asin == "B0FR4NC3D1").LocalItemId);
        Assert.Equal(byTitle.ItemId, Assert.Single(merged, i => i.Asin == "B0F0NDAT10").LocalItemId);
        Assert.Equal(CatalogAvailability.Missing, Assert.Single(merged, i => i.Asin == "B0OTHER000").Availability);
    }

    [Fact]
    public void Merge_LocalOnlyItems_AreKeptAsOnDisk()
    {
        var withTmdb = new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Movie, Title = "Home video", TmdbId = 42 };
        var withoutIds = new LocalCatalogEntry { ItemId = _localMatrixId, Kind = CatalogItemKind.Movie, Title = "Holiday 2019" };

        var merged = CatalogMerger.Merge([withTmdb, withoutIds], [], [], 20);

        Assert.Equal(["tmdb:movie:42", "library:" + _localMatrixId.ToString("N")], merged.Select(i => i.Key));
        Assert.All(merged, i => Assert.Equal(CatalogAvailability.OnDisk, i.Availability));
    }

    [Fact]
    public void Merge_RespectsLimit()
    {
        var tmdb = Enumerable.Range(1, 30).Select(i => TmdbMovie(i, "Film " + i, 2000)).ToArray();

        Assert.Equal(5, CatalogMerger.Merge([], tmdb, [], 5).Count);
    }

    [Theory]
    [InlineData("Amélie", "amelie")]
    [InlineData("  The   Matrix: Reloaded! ", "the matrix reloaded")]
    [InlineData("Café Noël", "cafe noel")]
    [InlineData(null, "")]
    public void Normalize_IgnoresCaseAccentsAndPunctuation(string? input, string expected)
        => Assert.Equal(expected, CatalogMerger.Normalize(input));
}
