using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Pufferfin.Catalog.Books;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Models;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Ebooks;
using Pufferfin.Server.Tests.Audiobooks;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Ebooks;

public sealed class ShelfmarkBookCatalogTests : IDisposable
{
    private readonly StoreTestContext _context = new();
    private readonly FakeArrServer _shelfmark = new();
    private readonly IPufferfinConfigurationStore _store;

    public ShelfmarkBookCatalogTests()
    {
        _store = _context.Start();
    }

    private ShelfmarkBookCatalog Catalog => new(_store, new ShelfmarkClient(FakeArrServer.CreateHttp(_shelfmark)), _context.Logs.CreateLogger<ShelfmarkBookCatalog>());

    public void Dispose()
    {
        _shelfmark.Dispose();
        _context.Dispose();
    }

    private void Configure()
        => _store.Update(StoreTestContext.Changes((SettingKeys.ShelfmarkUrl, "http://shelfmark:8084"), (SettingKeys.ShelfmarkApiKey, AudiobookTestKit.ShelfmarkKey)));

    private void Answer(JsonObject answer, HttpStatusCode status = HttpStatusCode.OK)
        => _shelfmark.Overrides.Add((method, path, _) => method == HttpMethod.Get && path.StartsWith("metadata/search?", StringComparison.Ordinal) ? FakeArrServer.Json(answer, status) : null);

    [Fact]
    public async Task ShelfmarkNotConfigured_BooksAreOff_WithoutACall()
    {
        var failure = await Assert.ThrowsAsync<CatalogSourceException>(() => Catalog.SearchAsync(new BookQuery { Query = "dune" }, TestContext.Current.CancellationToken));

        Assert.Equal(CatalogSourceStatus.Disabled, failure.Status);
        Assert.Empty(_shelfmark.Calls);
    }

    [Fact]
    public async Task TheBooks_CarryTheirProviderAndId_AndAClientUsableCover()
    {
        Configure();
        Answer(new JsonObject
        {
            ["provider"] = "hardcover",
            ["books"] = new JsonArray(EbookTestKit.Book(), EbookTestKit.Book("Dune Omnibus", language: "eng")),
            ["page"] = 1,
            ["has_more"] = false
        });

        var books = await Catalog.SearchAsync(new BookQuery { Query = "dune", Limit = 10, Page = 2 }, TestContext.Current.CancellationToken);

        var dune = books[0];
        Assert.Equal("shelfmark:hardcover:312460", dune.Key);
        Assert.Equal(CatalogItemKind.Book, dune.Kind);
        Assert.Equal("hardcover", dune.BookProvider);
        Assert.Equal("312460", dune.BookId);
        Assert.Equal(["Frank Herbert"], dune.Authors);
        Assert.Equal("Dune, 1", dune.SeriesName);
        Assert.Equal("fr", dune.Language);
        Assert.Equal(1965, dune.Year);
        Assert.Equal(CatalogAvailability.Missing, dune.Availability);
        Assert.Equal([CatalogSource.Shelfmark], dune.Sources);

        // The Shelfmark cover cache path needs the key: the client gets the original instead.
        Assert.Equal("https://assets.hardcover.app/dune.jpg", dune.ImageUrl);
        Assert.True(books[1].IsCompleteEdition);

        var call = Assert.Single(_shelfmark.Calls);
        Assert.Contains("content_type=ebook", call.Path, StringComparison.Ordinal);
        Assert.Contains("query=dune", call.Path, StringComparison.Ordinal);
        Assert.Contains("limit=10", call.Path, StringComparison.Ordinal);
        Assert.Contains("page=2", call.Path, StringComparison.Ordinal);
        Assert.Equal(AudiobookTestKit.ShelfmarkKey, call.Request.Headers.GetValues("X-Api-Key").Single());
        Assert.DoesNotContain(AudiobookTestKit.ShelfmarkKey, JsonSerializer.Serialize(books), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALanguage_KeepsTheBooksInItAndThoseThatDoNotSay()
    {
        Configure();
        var unknown = EbookTestKit.Book("Dune Messiah");
        unknown["provider_id"] = "2";
        unknown.Remove("language");
        var english = EbookTestKit.Book("Dune", language: "en");
        english["provider_id"] = "3";
        Answer(new JsonObject { ["provider"] = "hardcover", ["books"] = new JsonArray(EbookTestKit.Book(), unknown, english) });

        var books = await Catalog.SearchAsync(new BookQuery { Query = "dune", Language = "fr" }, TestContext.Current.CancellationToken);

        Assert.Equal(["312460", "2"], books.Select(b => b.BookId));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CatalogSourceStatus.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CatalogSourceStatus.Unavailable)]
    public async Task AFailingShelfmark_IsASourceStatus_NeverARawError(HttpStatusCode status, CatalogSourceStatus expected)
    {
        Configure();
        Answer(new JsonObject { ["error"] = "No metadata provider configured", ["message"] = "key " + AudiobookTestKit.ShelfmarkKey }, status);

        var failure = await Assert.ThrowsAsync<CatalogSourceException>(() => Catalog.SearchAsync(new BookQuery { Query = "dune" }, TestContext.Current.CancellationToken));

        Assert.Equal(CatalogSource.Shelfmark, failure.CatalogSource);
        Assert.Equal(expected, failure.Status);
        Assert.DoesNotContain(AudiobookTestKit.ShelfmarkKey, failure.Message, StringComparison.Ordinal);
        Assert.All(_context.Logs.Entries, e => Assert.DoesNotContain(AudiobookTestKit.ShelfmarkKey, e, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("fr-FR", "fr")]
    [InlineData("fre", "fr")]
    [InlineData("fra", "fr")]
    [InlineData("French", "fr")]
    [InlineData("English", "en")]
    [InlineData("ger", "de")]
    [InlineData("Klingon", null)]
    [InlineData("", null)]
    public void TheLanguageOfAProvider_IsReadAsAnIsoCode(string language, string? expected)
        => Assert.Equal(expected, ShelfmarkBooks.LanguageCode(language));

    [Theory]
    [InlineData("https://covers.openlibrary.org/b/id/1-L.jpg", "https://covers.openlibrary.org/b/id/1-L.jpg")]
    [InlineData("http://covers.openlibrary.org/b/id/1-L.jpg", null)]
    [InlineData("/api/covers/x?url=aHR0cHM6Ly9leGFtcGxlLmNvbS9jLmpwZw", "https://example.com/c.jpg")]
    [InlineData("/api/covers/x?url=ZmlsZTovLy9ldGMvcGFzc3dk", null)]
    [InlineData("/api/covers/x", null)]
    [InlineData("/api/covers/x?url=***", null)]
    public void TheCover_IsAnHttpsUrl_OrNothing(string cover, string? expected)
        => Assert.Equal(expected, ShelfmarkBooks.CoverUrl(cover));
}
