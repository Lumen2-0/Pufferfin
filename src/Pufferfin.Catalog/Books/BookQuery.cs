namespace Pufferfin.Catalog.Books;

/// <summary>
/// A book search.
/// </summary>
public sealed class BookQuery
{
    /// <summary>
    /// Gets the free text: a title, an author, an ISBN.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets the ISO 639-1 language to keep (<c>fr</c>, <c>en</c>). A book whose provider gives no language is kept.
    /// Empty keeps every language.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the maximum number of results, which is also the size of a page.
    /// </summary>
    public int Limit { get; init; } = 20;

    /// <summary>
    /// Gets the page, from 1.
    /// </summary>
    public int Page { get; init; } = 1;
}
