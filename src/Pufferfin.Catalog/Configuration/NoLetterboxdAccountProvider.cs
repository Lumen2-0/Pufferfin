using System;

namespace Pufferfin.Catalog.Configuration;

/// <summary>
/// Default <see cref="ILetterboxdAccountProvider"/>: no user has a Letterboxd account until the user settings store replaces it.
/// </summary>
public sealed class NoLetterboxdAccountProvider : ILetterboxdAccountProvider
{
    /// <inheritdoc />
    public string? GetUsername(Guid userId) => null;
}
