using System.Collections.Generic;
using Pufferfin.Catalog.Models;

namespace Pufferfin.Catalog.Search;

/// <summary>
/// A universal search.
/// </summary>
public sealed class CatalogSearchRequest
{
    /// <summary>
    /// Gets the search text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Gets the kinds to search. Empty searches every kind.
    /// </summary>
    public IReadOnlyCollection<CatalogItemKind> Kinds { get; init; } = [];

    /// <summary>
    /// Gets the maximum number of entries.
    /// </summary>
    public int Limit { get; init; } = 20;

    /// <summary>
    /// Gets the language to keep for audiobooks. Empty keeps every language.
    /// </summary>
    public string? AudiobookLanguage { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a type-ahead suggestion: shorter deadline per source.
    /// </summary>
    public bool IsSuggestion { get; init; }
}
