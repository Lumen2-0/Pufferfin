using System.Linq;
using System.Text.Json.Nodes;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Tests.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Audiobooks;

public sealed class AudiobookReleaseSelectorTests
{
    private static readonly AudiobookMetadata _dune = RequestTestKit.FakeAudiobooks.Dune;

    public static ShelfmarkRelease Release(string title, int? seeders = 10, string? language = null, string? id = null)
    {
        var raw = new JsonObject
        {
            ["source"] = "prowlarr",
            ["source_id"] = id ?? title,
            ["title"] = title,
            ["seeders"] = seeders,
            ["language"] = language,
            ["content_type"] = "audiobook",
            ["protocol"] = "torrent",
            ["download_url"] = "https://tracker.example/download?passkey=SECRET-passkey-77"
        };
        return ShelfmarkRelease.From(raw)!;
    }

    [Fact]
    public void ACompleteEdition_IsNeverPickedAutomatically_EvenWithFarMoreSeeders()
    {
        var single = Release("Frank Herbert - Dune Tome 1 [FR]", seeders: 3);
        var complete = Release("Frank Herbert - Dune, l'Integrale (6 tomes) [FR]", seeders: 900);

        var selection = AudiobookReleaseSelector.Select([complete, single], _dune, "fr");

        Assert.Same(single, selection.Release);
        Assert.Null(selection.Problem);

        // The complete edition is still ranked, flagged, so it can be offered.
        Assert.Contains(selection.Ranked, r => r.Release == complete && r.IsCompleteEdition);
    }

    [Fact]
    public void OnlyCompleteEditions_PickNothing_AndNameTheBestOne()
    {
        var selection = AudiobookReleaseSelector.Select(
            [
                Release("Frank Herbert - Dune, l'Integrale (6 tomes) [FR]", seeders: 900),
                Release("Frank Herbert - Le cycle de Dune T1-T6 [FR]", seeders: 400),
                Release("Frank Herbert - Dune Saga Complete French 1-6", seeders: 50),
            ],
            _dune,
            "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.OnlyCompleteEditions, selection.Problem!.Code);

        // The best ranked one: it names the volume of the book.
        Assert.Equal("Frank Herbert - Le cycle de Dune T1-T6 [FR]", selection.Problem.Detail);
    }

    [Fact]
    public void RealisticReleases_AreRanked_ByTitle_ThenVolume_ThenLanguage_ThenSeeders()
    {
        var releases = new[]
        {
            Release("Isaac Asimov - Fondation [FR]", seeders: 1000),
            Release("Frank Herbert - Le Messie de Dune Tome 2 [FR]", seeders: 80),
            Release("Frank Herbert - Dune [ENG] Unabridged", seeders: 300),
            Release("Dune Audiobook", seeders: 900),
            Release("Frank Herbert - Dune, l'Integrale (6 tomes) FR", seeders: 500),
            Release("Frank Herbert - Dune Tome 1 [FR] [M4B]", seeders: 12),
            Release("Brian Herbert - Dreamer of Dune [FR]", seeders: 7),
            Release("Frank Herbert - Dune T1 (French audiobook) 64kbps", seeders: 40),
        };

        var ranked = AudiobookReleaseSelector.Rank(releases, _dune, "fr").Select(r => r.Release.Title).ToList();

        Assert.Equal(
            [
                "Frank Herbert - Dune T1 (French audiobook) 64kbps",
                "Frank Herbert - Dune Tome 1 [FR] [M4B]",
                "Frank Herbert - Dune, l'Integrale (6 tomes) FR",
                "Brian Herbert - Dreamer of Dune [FR]",
                "Dune Audiobook",
                "Frank Herbert - Dune [ENG] Unabridged",
                "Frank Herbert - Le Messie de Dune Tome 2 [FR]",
                "Isaac Asimov - Fondation [FR]",
            ],
            ranked);
        Assert.Equal("Frank Herbert - Dune T1 (French audiobook) 64kbps", AudiobookReleaseSelector.Select(releases, _dune, "fr").Release!.Title);
    }

    [Fact]
    public void TheLanguageShelfmarkReports_WinsOverTheTitle()
    {
        var english = Release("Frank Herbert - Dune Tome 1", seeders: 500, language: "en");
        var french = Release("Frank Herbert - Dune Tome 1", seeders: 5, language: "fr", id: "fr");

        Assert.Same(french, AudiobookReleaseSelector.Select([english, french], _dune, "fr").Release);
    }

    [Fact]
    public void OnlyAnotherLanguage_PicksNothing()
    {
        var selection = AudiobookReleaseSelector.Select([Release("Frank Herbert - Dune [ENG]", seeders: 500)], _dune, "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoAcceptableLanguage, selection.Problem!.Code);
    }

    [Fact]
    public void AnotherVolume_IsNeverPicked()
    {
        var selection = AudiobookReleaseSelector.Select([Release("Frank Herbert - Dune Tome 3 [FR]", seeders: 500)], _dune, "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoRelease, selection.Problem!.Code);
    }

    [Fact]
    public void ExcludedReleases_AreNotTakenAgain()
    {
        var failed = Release("Frank Herbert - Dune Tome 1 [FR]", seeders: 500, id: "failed");
        var other = Release("Frank Herbert - Dune T01 VF", seeders: 2, id: "other");

        Assert.Same(other, AudiobookReleaseSelector.Select([failed, other], _dune, "fr", ["failed"]).Release);
    }

    [Theory]
    [InlineData("Harry Potter - L'Integrale")]
    [InlineData("J.K. Rowling - Harry Potter 1-7 [FR]")]
    [InlineData("Harry Potter Books 1 - 7 Unabridged")]
    [InlineData("Le Seigneur des Anneaux Tomes 1 a 3")]
    [InlineData("The Wheel of Time Complete Series")]
    [InlineData("Discworld Box Set")]
    [InlineData("Les Rougon-Macquart, coffret 20 romans")]
    [InlineData("The Expanse 9 books")]
    [InlineData("Foundation Omnibus")]
    [InlineData("Dune T01 - T06 (French)")]
    public void CompleteEditions_AreRecognised(string title)
        => Assert.True(AudiobookReleaseSelector.IsCompleteEdition(title), title);

    [Theory]
    [InlineData("Frank Herbert - Dune Tome 1 [FR]")]
    [InlineData("Harry Potter a l'ecole des sorciers")]
    [InlineData("George Orwell - 1984 (2021) [FR]")]
    [InlineData("Joseph Heller - Catch-22")]
    [InlineData("Ray Bradbury - Fahrenheit 451")]
    [InlineData("Podcast 2021-03-05 Dune")]
    [InlineData("Victor Hugo - Les Miserables Tome 2")]
    public void SingleBooks_AreNotTakenForCompleteEditions(string title)
        => Assert.False(AudiobookReleaseSelector.IsCompleteEdition(title), title);

    [Fact]
    public void ABookWhoseTitleSoundsLikeACompleteEdition_IsNotOne()
        => Assert.False(AudiobookReleaseSelector.IsCompleteEdition("Jane Doe - The Complete Guide to Gardening", "The Complete Guide to Gardening"));

    [Theory]
    [InlineData("Dune Tome 2", "2")]
    [InlineData("Dune T02", "2")]
    [InlineData("Dune, Book 3", "3")]
    [InlineData("Dune #4", "4")]
    [InlineData("Dune Vol. 5", "5")]
    [InlineData("Dune Tome 1.5", "1.5")]
    public void Volumes_AreRead(string title, string volume)
        => Assert.Contains(volume, AudiobookReleaseSelector.Volumes(title));

    [Theory]
    [InlineData("Dune [FR]", "fr")]
    [InlineData("Dune (French)", "fr")]
    [InlineData("Dune VF", "fr")]
    [InlineData("Dune [ENG]", "en")]
    [InlineData("Le Comte de Monte-Cristo", null)]
    [InlineData("It - Stephen King", null)]
    [InlineData("Dune [FR] [ENG]", null)]
    public void TheLanguageOfATitle_IsRead_OnlyWhenUnambiguous(string title, string? language)
        => Assert.Equal(language, AudiobookReleaseSelector.TitleLanguage(title));
}
