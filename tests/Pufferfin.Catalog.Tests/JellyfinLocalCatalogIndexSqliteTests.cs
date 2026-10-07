using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Server.Implementations.Data;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Xunit;

namespace Pufferfin.Catalog.Tests;

/// <summary>
/// Runs the index against the real Jellyfin item repository on SQLite, so the items come back the way the
/// library loads them: a mock that hands over fully built items cannot see a field the repository leaves out.
/// </summary>
public sealed class JellyfinLocalCatalogIndexSqliteTests : IDisposable
{
    private const string MovieType = "MediaBrowser.Controller.Entities.Movies.Movie";
    private const string SeriesType = "MediaBrowser.Controller.Entities.TV.Series";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _dbOptions;
    private readonly User _user = new("viewer", "auth-provider", "reset-provider");
    private readonly JellyfinLocalCatalogIndex _index;

    private readonly Guid _inception = Guid.NewGuid();
    private readonly Guid _dune = Guid.NewGuid();
    private readonly Guid _breakingBad = Guid.NewGuid();

    public JellyfinLocalCatalogIndexSqliteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<JellyfinDbContext>().UseSqlite(_connection).Options;

        using (var context = CreateDbContext())
        {
            context.Database.EnsureCreated();
            context.Users.Add(_user);
            AddItem(context, _inception, MovieType, "Inception", ("Tmdb", "27205"), ("Imdb", "tt1375666"));
            AddItem(context, _dune, MovieType, "Dune", ("Tmdb", "438631"));
            AddItem(context, _breakingBad, SeriesType, "Breaking Bad", ("Tmdb", "1396"), ("Tvdb", "81189"));

            // A film with no TMDB id, which no lookup may return.
            AddItem(context, Guid.NewGuid(), MovieType, "Home video");
            context.UserData.Add(new UserData
            {
                ItemId = _inception,
                UserId = _user.Id,
                CustomDataKey = _inception.ToString("N"),
                Played = true,
                Item = null!,
                User = null!
            });
            context.SaveChanges();
        }

        var factory = new Mock<IDbContextFactory<JellyfinDbContext>>();
        factory.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);
        var configuration = new Mock<IServerConfigurationManager>();
        configuration.Setup(c => c.Configuration).Returns(new ServerConfiguration());
        var repository = new BaseItemRepository(
            factory.Object,
            Mock.Of<IServerApplicationHost>(),
            new ItemTypeLookup(),
            configuration.Object,
            NullLogger<BaseItemRepository>.Instance);

        // The library manager adds the user's libraries to a query, then hands it to the repository as is.
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) => repository.GetItemList(query));
        var userManager = new Mock<IUserManager>();
        userManager.Setup(m => m.GetUserById(_user.Id)).Returns(_user);
        _index = new JellyfinLocalCatalogIndex(Mock.Of<ISearchManager>(), libraryManager.Object, userManager.Object);
    }

    [Fact]
    public void FindByTmdbIds_OneMovieOnDisk_FindsItWithItsLocalItem()
    {
        var found = _index.FindByTmdbIds(CatalogItemKind.Movie, [27205], _user.Id);

        var entry = Assert.Single(found);
        Assert.Equal(27205, entry.Key);
        Assert.Equal(_inception, entry.Value.ItemId);
        Assert.Equal("Inception", entry.Value.Title);
        Assert.Equal(27205, entry.Value.TmdbId);
    }

    [Fact]
    public void FindByTmdbIds_AListOfTitles_FindsTheOnesOnDiskOnly()
    {
        var found = _index.FindByTmdbIds(CatalogItemKind.Movie, [27205, 438631, 157336], _user.Id);

        Assert.Equal(new Dictionary<int, Guid> { [27205] = _inception, [438631] = _dune }, found.ToDictionary(e => e.Key, e => e.Value.ItemId));
    }

    [Fact]
    public void FindByTmdbIds_Series_FindsItAndNotAMovie()
    {
        Assert.Equal(_breakingBad, Assert.Single(_index.FindByTmdbIds(CatalogItemKind.Series, [1396, 27205], _user.Id)).Value.ItemId);
    }

    [Fact]
    public void GetPlayedMovieTmdbIds_ReturnsTheTmdbIdsOfTheFilmsPlayed()
    {
        Assert.Equal([27205], _index.GetPlayedMovieTmdbIds(_user.Id));
    }

    public void Dispose() => _connection.Dispose();

    private JellyfinDbContext CreateDbContext() => new(
        _dbOptions,
        NullLogger<JellyfinDbContext>.Instance,
        new SqliteDatabaseProvider(Mock.Of<IApplicationPaths>(), NullLogger<SqliteDatabaseProvider>.Instance),
        new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));

    private static void AddItem(JellyfinDbContext context, Guid id, string type, string name, params (string Provider, string Value)[] providerIds)
    {
        // Every library item has its own presentation key; a query made for a user groups the items by it.
        context.BaseItems.Add(new BaseItemEntity { Id = id, Type = type, Name = name, PresentationUniqueKey = id.ToString("N") });
        foreach (var (provider, value) in providerIds)
        {
            context.BaseItemProviders.Add(new BaseItemProvider { ItemId = id, Item = null!, ProviderId = provider, ProviderValue = value });
        }
    }
}
