using System;
using Pufferfin.Catalog.Configuration;
using Pufferfin.Server.Users;

namespace Pufferfin.Server.Catalog;

/// <summary>
/// Reads the Letterboxd account each user set in their own Pufferfin settings.
/// </summary>
public sealed class StoreLetterboxdAccountProvider : ILetterboxdAccountProvider
{
    private readonly IPufferfinUserSettingsStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreLetterboxdAccountProvider"/> class.
    /// </summary>
    /// <param name="store">The user settings store.</param>
    public StoreLetterboxdAccountProvider(IPufferfinUserSettingsStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public string? GetUsername(Guid userId) => _store.Get(userId).LetterboxdUsername;
}
