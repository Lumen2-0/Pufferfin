using System;

namespace Pufferfin.Catalog.Configuration;

/// <summary>
/// Supplies the optional Letterboxd account of a user, set in the user's own settings.
/// </summary>
public interface ILetterboxdAccountProvider
{
    /// <summary>
    /// Gets the Letterboxd username of a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The username, or <c>null</c> when the user has not linked an account.</returns>
    string? GetUsername(Guid userId);
}
