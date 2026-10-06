using System.Linq;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;
using Xunit;

namespace Pufferfin.Server.Tests.Arr;

public sealed class MediaLayoutTests
{
    private const int Animation = 16;
    private const int Documentary = 99;
    private const int Drama = 18;

    private static ArrRootFolder[] Folders(params string[] paths) => paths.Select(p => new ArrRootFolder { Path = p }).ToArray();

    [Theory]
    [InlineData(new[] { Drama }, new[] { "US" }, "en", MediaCategory.Standard)]
    [InlineData(new[] { Animation }, new[] { "US" }, "en", MediaCategory.Standard)]
    [InlineData(new[] { Animation }, new[] { "JP" }, "ja", MediaCategory.Anime)]
    [InlineData(new[] { Animation }, new string[0], "ja", MediaCategory.Anime)]
    [InlineData(new[] { Animation, Drama }, new[] { "JP" }, "en", MediaCategory.Anime)]
    [InlineData(new[] { Documentary }, new[] { "FR" }, "fr", MediaCategory.Documentary)]
    [InlineData(new[] { Documentary, Animation }, new[] { "JP" }, "ja", MediaCategory.Documentary)]
    public void Classify_UsesTheTmdbGenresAndCountry(int[] genres, string[] countries, string language, MediaCategory expected)
        => Assert.Equal(expected, MediaLayout.Classify(genres, countries, language));

    [Fact]
    public void PickRootFolder_PrefersThePufferfinFolderOfTheCategory()
    {
        var folders = Folders("/media/movies", "/media/anime-movies", "/media/documentary-movies");

        Assert.Equal("/media/anime-movies", MediaLayout.PickRootFolder(MediaCategory.Anime, MediaLayout.RadarrRootFolders, folders));
        Assert.Equal("/media/documentary-movies", MediaLayout.PickRootFolder(MediaCategory.Documentary, MediaLayout.RadarrRootFolders, folders));
        Assert.Equal("/media/movies", MediaLayout.PickRootFolder(MediaCategory.Standard, MediaLayout.RadarrRootFolders, folders));
    }

    [Fact]
    public void PickRootFolder_FollowsAFolderLayoutMadeByHand()
    {
        var folders = Folders("/data/Anime", "/data/Films", "/data/Documentaries");

        Assert.Equal("/data/Anime", MediaLayout.PickRootFolder(MediaCategory.Anime, MediaLayout.RadarrRootFolders, folders));
        Assert.Equal("/data/Documentaries", MediaLayout.PickRootFolder(MediaCategory.Documentary, MediaLayout.RadarrRootFolders, folders));
        Assert.Equal("/data/Films", MediaLayout.PickRootFolder(MediaCategory.Standard, MediaLayout.RadarrRootFolders, folders));
    }

    [Fact]
    public void PickRootFolder_FilesAnAnimeWithTheStandardTitles_WhenThereIsNoAnimeFolder()
        => Assert.Equal("/data/movies", MediaLayout.PickRootFolder(MediaCategory.Anime, MediaLayout.RadarrRootFolders, Folders("/data/movies")));

    [Fact]
    public void PickRootFolder_ReturnsNull_WithoutAnyFolder()
        => Assert.Null(MediaLayout.PickRootFolder(MediaCategory.Standard, MediaLayout.RadarrRootFolders, Folders()));

    [Theory]
    [InlineData("fr", "French")]
    [InlineData("FR", "French")]
    [InlineData("pt-BR", "Portuguese (Brazil)")]
    [InlineData("fr-CA", "French")]
    [InlineData("xx", null)]
    public void ArrLanguages_SpellTheLanguageLikeRadarrAndSonarr(string code, string? expected)
        => Assert.Equal(expected, ArrLanguages.GetName(code));
}
