using Pufferfin.Catalog.Library;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class AudiobookFoldersTests
{
    [Theory]
    [InlineData("/books/Albert Camus/L'Étranger/L'Étranger - Part 01.mp3", "l étranger")]
    [InlineData("L'Étranger - Part 13.mp3", "l étranger")]
    [InlineData("Harry Potter and the Philosopher's Stone, Book 1 - Part 001.mp3", "harry potter and the philosopher s stone")]
    [InlineData("Dune - Chapitre 3.m4a", "dune")]
    [InlineData("Dune CD2 Track 04.mp3", "dune")]
    [InlineData("01.mp3", "")]
    public void Stem_DropsWhatTellsThePartsApart(string path, string expected)
        => Assert.Equal(expected, AudiobookFolders.GetStem(path));

    [Fact]
    public void Parts_OfOneBook_AreOneBook()
        => Assert.True(AudiobookFolders.AreOneBook(["L'Étranger - Part 01.mp3", "L'Étranger - Part 02.mp3", "L'Étranger - Part 13.mp3"]));

    [Fact]
    public void Different_Books_InOneFolder_StayApart()
        => Assert.False(AudiobookFolders.AreOneBook(["Dune.m4b", "Le Messie de Dune.m4b"]));

    [Fact]
    public void Files_NamedByTheirNumbersOnly_StayApart()
        => Assert.False(AudiobookFolders.AreOneBook(["Book 1.m4b", "Book 2.m4b"]));

    [Fact]
    public void A_SingleFile_IsNotABookInParts()
        => Assert.False(AudiobookFolders.AreOneBook(["Dune.m4b"]));
}
