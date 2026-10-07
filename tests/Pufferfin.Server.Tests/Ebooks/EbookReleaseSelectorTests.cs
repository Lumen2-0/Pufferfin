using System.Linq;
using System.Threading.Tasks;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Ebooks;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Tests.Audiobooks;
using Pufferfin.Server.Tests.Requests;
using Xunit;

namespace Pufferfin.Server.Tests.Ebooks;

public sealed class EbookReleaseSelectorTests
{
    private static readonly EbookMetadata _dune = RequestTestKit.FakeEbooks.Dune;

    private static ShelfmarkRelease Release(string title, int? seeders = 10, string? format = "epub", string? language = null, string contentType = "ebook", string protocol = "torrent", string? id = null)
        => ShelfmarkRelease.From(EbookTestKit.Release(title, seeders, id, format, language, contentType, protocol))!;

    [Fact]
    public void AnOmnibus_IsNeverPickedAutomatically_EvenWithFarMoreSeedersAndABetterFormat()
    {
        var single = Release("Frank Herbert - Dune (Tome 1) [FR]", seeders: 2, format: "pdf");
        var omnibus = Release("Frank Herbert - Dune Omnibus (Books 1-3) [FR]", seeders: 800, format: "epub");
        var boxSet = Release("Frank Herbert - Le cycle de Dune, l'Integrale [FR]", seeders: 500, format: "epub");

        var selection = EbookReleaseSelector.Select([omnibus, boxSet, single], _dune, "fr");

        Assert.Same(single, selection.Release);
        Assert.Null(selection.Problem);

        // Offered, never imposed: both stay in the ranking, flagged.
        Assert.Contains(selection.Ranked, r => r.Release == omnibus && r.IsCompleteEdition);
        Assert.Contains(selection.Ranked, r => r.Release == boxSet && r.IsCompleteEdition);
    }

    [Fact]
    public void OnlyOmnibuses_PickNothing_AndSayWhy()
    {
        var selection = EbookReleaseSelector.Select(
            [
                Release("Frank Herbert - Dune Omnibus (Books 1-3) [FR]", seeders: 800),
                Release("Frank Herbert - Dune Box Set 1-6 epub", seeders: 40),
            ],
            _dune,
            "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.OnlyCompleteEditions, selection.Problem!.Code);
    }

    [Fact]
    public void AnOmnibusAskedForItself_IsFulfilledByAnOmnibusOnly()
    {
        var book = _dune with { Title = "Dune Omnibus", SeriesPosition = null, IsCompleteEdition = true };
        var single = Release("Frank Herbert - Dune [FR]", seeders: 900);
        var omnibus = Release("Frank Herbert - Dune Omnibus [FR]", seeders: 5);

        var selection = EbookReleaseSelector.Select([single, omnibus], book, "fr");

        Assert.Same(omnibus, selection.Release);
    }

    [Fact]
    public void TheRanking_OnARealisticSet_IsTitleThenVolumeThenLanguageThenSourceQuality()
    {
        // What Prowlarr, IRC and a direct download source answer for "Dune" in French.
        var messiah = Release("Frank Herbert - Le Messie de Dune (Dune T2) [FR] epub", seeders: 300, id: "messiah");
        var volume2 = Release("Frank Herbert - Dune Tome 2 [FR]", seeders: 250, id: "volume2");
        var english = Release("Frank Herbert - Dune (English) epub", seeders: 999, id: "english");
        var pdfMany = Release("Frank Herbert - Dune T1 [FR]", seeders: 400, format: "pdf", id: "pdf");
        var epubFew = Release("Frank Herbert - Dune T1 [FR]", seeders: 3, format: "epub", id: "epub");
        var zip = Release("Frank Herbert - Dune T1 [FR]", seeders: 800, format: "zip", id: "zip");
        var unknown = Release("Frank Herbert - Dune T1 [FR]", seeders: 900, format: null, id: "unknown");
        var direct = Release("Frank Herbert - Dune", seeders: null, format: "epub", language: "fr", protocol: "http", id: "direct");
        var mobi = Release("Frank Herbert - Dune T1 [FR]", seeders: 950, format: "mobi", id: "mobi");

        var ranked = EbookReleaseSelector.Rank([messiah, volume2, english, pdfMany, epubFew, zip, unknown, direct, mobi], _dune, "fr");
        var order = ranked.Select(r => r.Release.SourceId).ToList();

        // Exact title, right volume, right language: EPUB before PDF before an archive before a release that names no
        // format, whatever the seeders. MOBI is never in the ranking.
        Assert.Equal(["epub", "pdf", "zip", "unknown", "direct", "english", "volume2", "messiah"], order);
        Assert.Equal("epub", EbookReleaseSelector.Select(ranked.Select(r => r.Release), _dune, "fr").Release!.SourceId);
    }

    [Fact]
    public void SeedersDecideBetweenTwoReleasesOfTheSameFormat()
    {
        var few = Release("Frank Herbert - Dune T1 [FR]", seeders: 3, id: "few");
        var many = Release("Frank Herbert - Dune T1 [FR]", seeders: 30, id: "many");

        Assert.Equal("many", EbookReleaseSelector.Select([few, many], _dune, "fr").Release!.SourceId);
    }

    [Fact]
    public void AnotherBookOfTheSeries_OrAnotherLanguage_IsNeverTaken()
    {
        var selection = EbookReleaseSelector.Select(
            [
                Release("Frank Herbert - Dune Tome 2 [FR]", seeders: 500),
                Release("Frank Herbert - Dune (English)", seeders: 500),
            ],
            _dune,
            "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoAcceptableLanguage, selection.Problem!.Code);
    }

    [Fact]
    public void AudiobookReleases_NeverComeIntoTheBookResults()
    {
        var audiobook = Release("Frank Herbert - Dune T1 [FR]", seeders: 900, format: "m4b", contentType: "audiobook", id: "audio");
        var ebook = Release("Frank Herbert - Dune T1 [FR]", seeders: 1, id: "ebook");

        var selection = EbookReleaseSelector.Select([audiobook, ebook], _dune, "fr");

        Assert.Equal("ebook", selection.Release!.SourceId);
        Assert.DoesNotContain(selection.Ranked, r => r.Release.SourceId == "audio");
    }

    [Fact]
    public async Task TheShelfmarkClient_KeepsOnlyTheContentTypeItSearched_BothWays()
    {
        // Shelfmark filters on the content type; a release that still says it is of the other one is dropped.
        using var kit = new EbookTestKit();
        kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", contentType: "ebook", id: "ebook"));
        kit.Releases.Add(EbookTestKit.Release("Frank Herbert - Dune Tome 1 [FR]", format: "m4b", contentType: "audiobook", id: "audio"));
        var client = new ShelfmarkClient(TestHelpers.FakeArrServer.CreateHttp(kit.Shelfmark));
        var shelfmark = AudiobookTestKit.Context().Settings.GetService(PufferfinService.Shelfmark)!;

        var audiobooks = await client.SearchAudiobookReleasesAsync(shelfmark, RequestTestKit.FakeAudiobooks.Dune, "fr", TestContext.Current.CancellationToken);
        var ebooks = await client.SearchEbookReleasesAsync(shelfmark, _dune, "fr", TestContext.Current.CancellationToken);

        Assert.Equal(["audio"], audiobooks.Select(r => r.SourceId));
        Assert.Equal(["ebook"], ebooks.Select(r => r.SourceId));
    }

    [Fact]
    public void OnlyFormatsTheClientCannotOpen_PickNothing_AndSayWhich()
    {
        var selection = EbookReleaseSelector.Select(
            [
                Release("Frank Herbert - Dune T1 [FR]", format: "mobi"),
                Release("Frank Herbert - Dune T1 [FR]", format: "azw3"),
            ],
            _dune,
            "fr");

        Assert.Null(selection.Release);
        Assert.Equal(RequestProblemCode.NoRelease, selection.Problem!.Code);
        Assert.Contains("mobi", selection.Problem.Message, System.StringComparison.Ordinal);
        Assert.Contains("azw3", selection.Problem.Message, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Frank Herbert - Dune (epub, mobi, azw3)", "epub")]
    [InlineData("Frank Herbert - Dune [FR] PDF", "pdf")]
    [InlineData("Dune T01 cbz", "cbz")]
    [InlineData("Frank Herbert - Dune (mobi)", "mobi")]
    [InlineData("Frank Herbert - Dune [FR]", null)]
    public void TheFormat_IsReadFromTheTitle_WhenShelfmarkDoesNotSay(string title, string? format)
        => Assert.Equal(format, EbookFormats.FormatOf(Release(title, format: null)));
}
