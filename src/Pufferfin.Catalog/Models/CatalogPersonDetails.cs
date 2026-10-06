using System;
using System.Collections.Generic;

namespace Pufferfin.Catalog.Models;

/// <summary>
/// The detailed sheet of a person with their filmography.
/// </summary>
public sealed class CatalogPersonDetails
{
    /// <summary>
    /// Gets the TMDB id.
    /// </summary>
    public required int TmdbId { get; init; }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the biography, in the first configured language that has one, or in English.
    /// </summary>
    public string? Biography { get; init; }

    /// <summary>
    /// Gets the birth date.
    /// </summary>
    public DateOnly? Birthday { get; init; }

    /// <summary>
    /// Gets the death date.
    /// </summary>
    public DateOnly? Deathday { get; init; }

    /// <summary>
    /// Gets the place of birth.
    /// </summary>
    public string? PlaceOfBirth { get; init; }

    /// <summary>
    /// Gets the department the person is known for.
    /// </summary>
    public string? KnownForDepartment { get; init; }

    /// <summary>
    /// Gets the portrait URL.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// Gets the filmography (films and series), most recent first, with the availability of each title.
    /// </summary>
    public IReadOnlyList<CatalogItem> Filmography { get; init; } = [];

    /// <summary>
    /// Gets whether the person is known to the local library.
    /// </summary>
    public required CatalogAvailability Availability { get; init; }

    /// <summary>
    /// Gets the id of the local person item.
    /// </summary>
    public Guid? LocalItemId { get; init; }
}
