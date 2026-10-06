using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Moq;
using Pufferfin.Catalog.Library;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class JellyfinLocalCatalogIndexTests
{
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly List<InternalItemsQuery> _queries = [];

    private JellyfinLocalCatalogIndex CreateIndex(Func<InternalItemsQuery, IReadOnlyList<BaseItem>> items)
    {
        _libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) =>
            {
                _queries.Add(query);
                return items(query);
            });
        return new JellyfinLocalCatalogIndex(Mock.Of<ISearchManager>(), _libraryManager.Object, _userManager.Object);
    }

    [Fact]
    public void GetSeriesContent_UnknownUser_ReturnsNothing_WithoutAnUnfilteredQuery()
    {
        var index = CreateIndex(_ => throw new InvalidOperationException("No query expected."));

        var content = index.GetSeriesContent(1396, Guid.NewGuid());

        Assert.Same(LocalSeriesContent.Empty, content);
        Assert.Empty(_queries);
    }

    [Fact]
    public void GetSeriesContent_QueriesAsTheUser_SkipsVirtualEpisodes_AndPlacesEachEpisode()
    {
        var user = new User("viewer", "auth", "reset") { MaxParentalRatingScore = 12 };
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var series = new Series { Id = Guid.NewGuid(), Name = "Breaking Bad" };
        var season = new Season { Id = Guid.NewGuid(), IndexNumber = 1, Name = "Season 1" };
        var numbered = new Episode { Id = Guid.NewGuid(), ParentIndexNumber = 1, IndexNumber = 1, IndexNumberEnd = 2, Name = "Pilot", RunTimeTicks = TimeSpan.FromMinutes(58.6).Ticks };
        var placedBySeason = new Episode { Id = Guid.NewGuid(), SeasonId = season.Id, IndexNumber = 3, PremiereDate = new DateTime(2008, 2, 10, 2, 0, 0, DateTimeKind.Utc) };
        var unplaced = new Episode { Id = Guid.NewGuid(), IndexNumber = 4 };
        var index = CreateIndex(query => query.IncludeItemTypes.Contains(BaseItemKind.Series)
            ? [series]
            : [season, numbered, placedBySeason, unplaced]);

        var content = index.GetSeriesContent(1396, user.Id);

        Assert.Equal(2, _queries.Count);
        Assert.All(_queries, q =>
        {
            Assert.Same(user, q.User);
            Assert.Equal(12, q.MaxParentalRating?.Score);
        });
        Assert.Equal(["1396"], _queries[0].HasAnyProviderIds!["Tmdb"]);
        Assert.Equal([series.Id], _queries[1].AncestorIds);
        Assert.False(_queries[1].IsVirtualItem);

        Assert.Equal(series.Id, content.SeriesItemId);
        Assert.Equal(season.Id, Assert.Single(content.Seasons).ItemId);
        Assert.Equal([numbered.Id, placedBySeason.Id], content.Episodes.Select(e => e.ItemId));
        Assert.Equal(2, content.Episodes[0].EpisodeNumberEnd);
        Assert.Equal(59, content.Episodes[0].RuntimeMinutes);
        Assert.Equal(1, content.Episodes[1].SeasonNumber);
        Assert.Equal(new DateOnly(2008, 2, 10), content.Episodes[1].AirDate);
    }
}
