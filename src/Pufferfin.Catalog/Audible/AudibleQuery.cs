namespace Pufferfin.Catalog.Audible;

/// <summary>
/// An audiobook search. At least one of the text criteria is required.
/// </summary>
public sealed class AudibleQuery
{
    /// <summary>
    /// Gets the free text, matched against title, author and series.
    /// </summary>
    public string? Keywords { get; init; }

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the author.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the series name.
    /// </summary>
    public string? Series { get; init; }

    /// <summary>
    /// Gets the language to keep (<c>fr</c>, <c>en-US</c> or an Audible name such as <c>french</c>). Empty keeps every language.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the maximum number of results.
    /// </summary>
    public int Limit { get; init; } = 20;

    /// <summary>
    /// Gets a value indicating whether the query has a text criterion.
    /// </summary>
    public bool HasCriteria => !string.IsNullOrWhiteSpace(Keywords)
        || !string.IsNullOrWhiteSpace(Title)
        || !string.IsNullOrWhiteSpace(Author)
        || !string.IsNullOrWhiteSpace(Series);
}
