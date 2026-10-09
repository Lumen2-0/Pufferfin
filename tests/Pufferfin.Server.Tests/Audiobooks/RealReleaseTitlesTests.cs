using System.Text.Json.Nodes;
using Pufferfin.Server.Audiobooks;
using Xunit;

namespace Pufferfin.Server.Tests.Audiobooks;

/// <summary>
/// Release names as the French trackers write them, which the ranking once read as other books: a scene group at the
/// end ("-NOTAG"), the volume after the name of the series ("The.Empyrean.3"), an audiobook filed with the ebooks.
/// </summary>
public sealed class RealReleaseTitlesTests
{
    private static readonly AudiobookMetadata _onyxStormAudio = new(
        "B0DGXYZ123", "Onyx Storm", ["Rebecca Yarros"], "The Empyrean", "3", "fr", null, "fr");

    private static readonly AudiobookMetadata _saleGosse = new(
        "B00LHPNBC6", "Sale gosse", ["Stephen King"], null, null, "fr", null, "fr");

    private static ShelfmarkRelease Release(string title, int seeders = 10, string contentType = "audiobook", string? format = null)
        => ShelfmarkRelease.From(new JsonObject
        {
            ["source"] = "prowlarr",
            ["source_id"] = title,
            ["title"] = title,
            ["seeders"] = seeders,
            ["content_type"] = contentType,
            ["format"] = format,
            ["protocol"] = "torrent"
        })!;

    [Fact]
    public void AnAudiobookWithASceneGroup_IsTaken()
    {
        var release = Release("Onyx.Storm.Rebecca.Yarros.2025.FR.[MP3]-NOTAG", 33);

        var selection = AudiobookReleaseSelector.Select([release], _onyxStormAudio, "fr");

        Assert.Same(release, selection.Release);
    }

    [Fact]
    public void AnAudiobookWithTheVolumeAfterTheSeries_IsTaken()
    {
        var release = Release("The.Empyrean.3.Onyx.Storm.Rebecca.Yarros.FR.[MP3.128KBPS]-NOTAG", 48);

        var selection = AudiobookReleaseSelector.Select([release], _onyxStormAudio, "fr");

        Assert.Same(release, selection.Release);
    }

    [Fact]
    public void AnotherVolumeAfterTheSeries_IsNotTaken()
    {
        var release = Release("The.Empyrean.2.Iron.Flame.Onyx.Storm.Rebecca.Yarros.FR.[MP3]-NOTAG", 48);

        Assert.Null(AudiobookReleaseSelector.Select([release], _onyxStormAudio, "fr").Release);
    }

    [Fact]
    public void AnAudiobookFiledWithTheEbooks_IsTaken_AndNeverTheEbook()
    {
        var ebook = Release("Stephen King Sale Gosse FRENCH EBOOK EPUB", 148, "book");
        var audio = Release("Stephen King - Sale gosse [2014] [mp3  128kbps]", 1, "book");

        Assert.False(AudiobookReleaseSelector.IsAudio(ebook));
        Assert.True(AudiobookReleaseSelector.IsAudio(audio));
        Assert.True(AudiobookReleaseSelector.HasAudioMarker(audio.Title));

        var selection = AudiobookReleaseSelector.Select([audio], _saleGosse, "fr");
        Assert.Same(audio, selection.Release);
    }

    [Theory]
    [InlineData("Frank Herbert - Dune [FR]")]
    [InlineData("The Amazing Spider-Man")]
    public void ATitleWithSpaces_KeepsItsDashes(string title)
        => Assert.True(AudiobookReleaseSelector.IsAudio(Release(title)));

    [Fact]
    public void ADashInATitleWithSpaces_IsNotAGroup()
    {
        var book = new AudiobookMetadata("B000000001", "Spider-Man", ["Stan Lee"], null, null, "en", null, "us");

        Assert.Equal(TitleMatch.Exact, AudiobookReleaseSelector.MatchTitle("Stan Lee - Spider-Man [mp3]", book));
    }
}
