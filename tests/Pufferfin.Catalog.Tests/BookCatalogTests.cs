using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Pufferfin.Catalog.Api;
using Pufferfin.Catalog.Books;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Search;
using Pufferfin.Catalog.Tests.TestSupport;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class BookCatalogTests
{
    private static readonly CatalogItem _dune = new()
    {
        Key = "shelfmark:hardcover:312460",
        Kind = CatalogItemKind.Book,
        Title = "Dune",
        Availability = CatalogAvailability.Missing,
        BookProvider = "hardcover",
        BookId = "312460",
        Authors = ["Frank Herbert"],
        Sources = [CatalogSource.Shelfmark]
    };

    private static readonly LocalCatalogEntry _localDune = new()
    {
        ItemId = Guid.NewGuid(),
        Kind = CatalogItemKind.Book,
        Title = "Dune"
    };

    private static readonly LocalCatalogEntry _localOnly = new()
    {
        ItemId = Guid.NewGuid(),
        Kind = CatalogItemKind.Book,
        Title = "Dune, carnets de tournage"
    };

    private static CatalogController Controller(CatalogTestHost host)
    {
        var controller = ActivatorUtilities.CreateInstance<CatalogController>(host.Services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, Guid.NewGuid().ToString("N"))], "Test"))
            }
        };
        return controller;
    }

    private static Task<ActionResult<CatalogListResponse>> Search(CatalogController controller, string query = "dune", int? page = null)
        => controller.SearchBooks(query, null, null, page, CancellationToken.None);

    [Fact]
    public async Task WithoutShelfmark_TheListIsEmpty_WithTheSourceDisabled_NotAnError()
    {
        using var host = new CatalogTestHost();
        host.Library.Items.Add(_localDune);

        var response = (await Search(Controller(host))).Value!;

        Assert.Empty(response.Items);
        Assert.Equal([new CatalogSourceReport(CatalogSource.Shelfmark, CatalogSourceStatus.Disabled)], response.Sources);
    }

    [Fact]
    public async Task TheBooks_AreMarkedOnDisk_AndCarryTheirProvider()
    {
        using var host = new CatalogTestHost(books: new FakeBooks { Books = [_dune] });
        host.Library.Items.Add(_localDune);
        host.Library.Items.Add(_localOnly);

        var response = (await Search(Controller(host))).Value!;

        var dune = response.Items.Single(i => i.Key == _dune.Key);
        Assert.Equal(CatalogAvailability.OnDisk, dune.Availability);
        Assert.Equal(_localDune.ItemId, dune.LocalItemId);
        Assert.Equal("hardcover", dune.BookProvider);
        Assert.Equal("312460", dune.BookId);
        Assert.Contains(response.Items, i => i.LocalItemId is { } id && id.Equals(_localOnly.ItemId));
        Assert.Equal(
            [new CatalogSourceReport(CatalogSource.Library, CatalogSourceStatus.Ok), new CatalogSourceReport(CatalogSource.Shelfmark, CatalogSourceStatus.Ok)],
            response.Sources);
    }

    [Fact]
    public async Task ALaterPage_DoesNotRepeatTheBooksFoundOnDiskOnly()
    {
        var books = new FakeBooks { Books = [_dune] };
        using var host = new CatalogTestHost(books: books);
        host.Library.Items.Add(_localOnly);

        var response = (await Search(Controller(host), page: 2)).Value!;

        Assert.Equal([_dune.Key], response.Items.Select(i => i.Key));
        Assert.Equal(2, books.Queries.Single().Page);
    }

    [Theory]
    [InlineData(CatalogSourceStatus.Unavailable)]
    [InlineData(CatalogSourceStatus.Unauthorized)]
    [InlineData(CatalogSourceStatus.TimedOut)]
    public async Task AFailingShelfmark_LeavesTheBooksOnDisk_WithTheSourceStatus(CatalogSourceStatus status)
    {
        using var host = new CatalogTestHost(books: new FakeBooks { Failure = status });
        host.Library.Items.Add(_localDune);

        var response = (await Search(Controller(host))).Value!;

        Assert.Equal(_localDune.ItemId, Assert.Single(response.Items).LocalItemId);
        Assert.Contains(new CatalogSourceReport(CatalogSource.Shelfmark, status), response.Sources);
    }

    [Fact]
    public async Task AnEmptySearch_IsRefused_WithoutACall()
    {
        var books = new FakeBooks();
        using var host = new CatalogTestHost(books: books);

        var response = await Search(Controller(host), "   ");

        Assert.IsType<BadRequestResult>(response.Result);
        Assert.Empty(books.Queries);
    }

    [Fact]
    public async Task TheUniversalSearch_KeepsItsKinds_WithoutBooks()
    {
        using var host = new CatalogTestHost();
        host.Library.Items.Add(_localDune);

        var response = await host.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = "dune" }, Guid.NewGuid(), CancellationToken.None);

        Assert.DoesNotContain(response.Items, i => i.Kind == CatalogItemKind.Book);
    }

    private sealed class FakeBooks : IBookCatalog
    {
        public IReadOnlyList<CatalogItem> Books { get; init; } = [];

        public CatalogSourceStatus? Failure { get; init; }

        public List<BookQuery> Queries { get; } = [];

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(BookQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Failure is { } status
                ? throw new CatalogSourceException(CatalogSource.Shelfmark, status)
                : Task.FromResult(Books);
        }
    }
}
