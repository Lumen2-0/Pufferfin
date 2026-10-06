using System.Linq;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Audiobooks;

/// <summary>
/// The rule of the owner: a complete edition is never the fallback of a volume, and a complete edition asked for
/// itself is taken.
/// </summary>
public sealed class CompleteEditionTests
{
    // Harry Potter, volume 5, in French.
    private static readonly AudiobookMetadata _volume5 = new(
        "B00HPFIVE1",
        "Harry Potter et l'Ordre du Phenix",
        ["J.K. Rowling"],
        "Harry Potter",
        "5",
        "fr",
        null,
        "fr");

    // The complete edition, as Audible lists it.
    private static readonly AudiobookMetadata _complete = new(
        "B00HPALL07",
        "Harry Potter - L'Integrale",
        ["J.K. Rowling"],
        "Harry Potter, 1-7",
        null,
        "fr",
        null,
        "fr",
        IsCompleteEdition: true);

    // Reported in review: numbered complete editions are common on the French market.
    private static readonly AudiobookMetadata _thronesFirst = new(
        "B0TRONE001",
        "Le Trone de fer - L'Integrale 1",
        ["George R. R. Martin"],
        "Le Trone de fer",
        null,
        "fr",
        null,
        "fr",
        IsCompleteEdition: true);

    private static ShelfmarkRelease Release(string title, int seeders) => AudiobookReleaseSelectorTests.Release(title, seeders);

    [Fact]
    public void AVolumeThatOnlyExistsInsideACompleteEdition_IsNotAvailable_AndNothingIsTaken()
    {
        var selection = AudiobookReleaseSelector.Select(
            [
                Release("J.K. Rowling - Harry Potter - L'Integrale 1-7 [FR]", 800),
                Release("Harry Potter Tomes 1 a 7 (French)", 300),
            ],
            _volume5,
            "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.OnlyCompleteEditions, selection.Problem!.Code);
        Assert.Contains("not available", selection.Problem.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TheVolumeItself_IsTaken_EvenWhenACompleteEditionHasFarMoreSeeders()
    {
        var volume = Release("J.K. Rowling - Harry Potter et l'Ordre du Phenix Tome 5 [FR]", 4);

        var selection = AudiobookReleaseSelector.Select([Release("Harry Potter - L'Integrale 1-7 [FR]", 800), volume], _volume5, "fr");

        Assert.Same(volume, selection.Release);
    }

    [Fact]
    public void ACompleteEditionAskedForItself_IsTaken_InTheRightLanguage_WithTheMostSeeders()
    {
        var expected = Release("Harry Potter Integrale 1-7 [FR]", 40);
        var selection = AudiobookReleaseSelector.Select(
            [
                Release("J.K. Rowling - Harry Potter 5 L'Ordre du Phenix [FR]", 900),
                Release("Harry Potter Integrale 1-7 [ENG]", 500),
                Release("Percy Jackson - L'Integrale [FR]", 1000),
                Release("Harry Potter - Integrale [FR]", 10),
                expected,
            ],
            _complete,
            "fr");

        Assert.Same(expected, selection.Release);
    }

    [Fact]
    public void ANumberedCompleteEdition_IsNeverReplacedByAnotherNumber_EvenWithMoreSeeders()
    {
        var first = Release("George R.R. Martin - Le Trone de fer - L'Integrale 1 [FR]", 10);

        var selection = AudiobookReleaseSelector.Select([Release("George R.R. Martin - Le Trone de fer - L'Integrale 2 [FR]", 100), first], _thronesFirst, "fr");

        Assert.Same(first, selection.Release);
        Assert.False(AudiobookReleaseSelector.IsBook("Le Trone de fer - L'Integrale 2 [FR]", _thronesFirst, "fr"));
        Assert.True(AudiobookReleaseSelector.IsBook("Le Trone de fer - L'Integrale 1 [FR]", _thronesFirst, "fr"));
    }

    [Fact]
    public void ACompleteEditionWithARange_IsNeverReplacedByAnotherRange_EvenWithMoreSeeders()
    {
        var whole = Release("Harry Potter 1-7 [FR]", 10);

        var selection = AudiobookReleaseSelector.Select([Release("J.K. Rowling - Harry Potter Books 1-3 [FR]", 900), whole], _complete, "fr");

        Assert.Same(whole, selection.Release);
        Assert.False(AudiobookReleaseSelector.IsBook("Harry Potter Books 1-3 [FR]", _complete, "fr"));
    }

    [Fact]
    public void OnlyAnotherCompleteEdition_IsNotTaken()
    {
        var selection = AudiobookReleaseSelector.Select([Release("Le Trone de fer - L'Integrale 2 [FR]", 100)], _thronesFirst, "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoRelease, selection.Problem!.Code);
    }

    [Fact]
    public void AWholeSeriesEdition_TakesTheSameVolumesCountedInsteadOfRanged_ButNoNumberedPart()
    {
        var whole = _complete with { Title = "Dune - L'Integrale", SeriesName = "Dune, 1-6" };
        var unnumbered = _complete with { Title = "Dune - L'Integrale", SeriesName = "Dune" };

        Assert.True(AudiobookReleaseSelector.IsBook("Frank Herbert - Dune, l'Integrale (6 tomes) [FR]", whole, "fr"));
        Assert.False(AudiobookReleaseSelector.IsBook("Frank Herbert - Dune, l'Integrale (3 tomes) [FR]", whole, "fr"));
        Assert.True(AudiobookReleaseSelector.IsBook("Frank Herbert - Dune, l'Integrale (6 tomes) [FR]", unnumbered, "fr"));
        Assert.False(AudiobookReleaseSelector.IsBook("Frank Herbert - Dune - L'Integrale 2 [FR]", unnumbered, "fr"));
    }

    [Fact]
    public void ACompleteEditionAskedForItself_IsNeverReplacedByAVolume()
    {
        var selection = AudiobookReleaseSelector.Select(
            [
                Release("J.K. Rowling - Harry Potter a l'ecole des sorciers Tome 1 [FR]", 900),
                Release("J.K. Rowling - Harry Potter 5 L'Ordre du Phenix [FR]", 900),
            ],
            _complete,
            "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoRelease, selection.Problem!.Code);
    }

    [Fact]
    public void ACompleteEditionAskedForItself_InAnotherLanguageOnly_IsNotTaken()
    {
        var selection = AudiobookReleaseSelector.Select([Release("Harry Potter Complete Collection 1-7 [ENG]", 900)], _complete, "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoAcceptableLanguage, selection.Problem!.Code);
    }

    [Theory]
    [InlineData("Harry Potter - L'Integrale 1-7 [FR]", true)]
    [InlineData("Harry Potter Integrale [ENG]", false)]
    [InlineData("Harry Potter 5 L'Ordre du Phenix [FR]", false)]
    [InlineData("Percy Jackson - L'Integrale [FR]", false)]
    public void AShelfmarkDownload_IsAdoptedForACompleteEdition_OnlyIfItIsOneOfTheSameSeries(string title, bool adopted)
        => Assert.Equal(adopted, AudiobookReleaseSelector.IsBook(title, _complete, "fr"));

    [Fact]
    public void AVolumeRequest_NeverAdoptsACompleteEditionInShelfmark()
        => Assert.False(AudiobookReleaseSelector.IsBook("Harry Potter - L'Integrale 1-7 [FR]", _volume5, "fr"));

    [Fact]
    public void TheRankingStillShowsTheCompleteEditions_ForTheSheet()
    {
        var selection = AudiobookReleaseSelector.Select([Release("Harry Potter - L'Integrale 1-7 [FR]", 800)], _volume5, "fr");

        Assert.True(selection.Ranked.Single().IsCompleteEdition);
    }
}
