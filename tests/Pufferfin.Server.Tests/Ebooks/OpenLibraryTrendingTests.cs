using Pufferfin.Server.Ebooks;
using Xunit;

namespace Pufferfin.Server.Tests.Ebooks;

public sealed class OpenLibraryTrendingTests
{
    [Fact]
    public void Parse_ReadsTheWorks_AsShelfmarkOpenLibraryBooks()
    {
        const string Json = """
            {"query":"trending","works":[
              {"key":"/works/OL82563W","title":"Harry Potter and the Philosopher's Stone","author_name":["J. K. Rowling"],"first_publish_year":1997,"cover_i":10521270},
              {"key":"/works/OL1W","title":"No cover","author_name":["Someone"]},
              {"key":"/works/OL2W","title":" ","cover_i":12},
              {"key":"/authors/OL3A","title":"Not a work","cover_i":13}
            ]}
            """;

        var book = Assert.Single(OpenLibraryTrending.Parse(Json));

        Assert.Equal(OpenLibraryTrending.Provider, book.Provider);
        Assert.Equal("OL82563W", book.BookId);
        Assert.Equal("Harry Potter and the Philosopher's Stone", book.Title);
        Assert.Equal(["J. K. Rowling"], book.Authors);
        Assert.Equal(1997, book.Year);
        Assert.Equal("https://covers.openlibrary.org/b/id/10521270-L.jpg", book.CoverUrl);

        // The key is the one of a book found by a search, so it opens the same sheet
        Assert.Equal("shelfmark:openlibrary:OL82563W", ShelfmarkBooks.ToCatalogItem(book).Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"works\":null}")]
    public void Parse_AnUnreadableAnswer_IsEmpty(string json)
        => Assert.Empty(OpenLibraryTrending.Parse(json));
}
