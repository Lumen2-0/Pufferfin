using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// The persisted Pufferfin configuration store, source of truth of the configuration.
/// </summary>
public interface IPufferfinConfigurationStore
{
    /// <summary>
    /// Raised after every successful save, with the new snapshot.
    /// </summary>
    event EventHandler<PufferfinConfigurationSnapshot>? Saved;

    /// <summary>
    /// Gets the current snapshot.
    /// </summary>
    PufferfinConfigurationSnapshot Current { get; }

    /// <summary>
    /// Validates and saves a partial update. The update is all or nothing.
    /// A key that is absent is left unchanged; a <c>null</c> value clears the setting;
    /// any other value replaces it.
    /// </summary>
    /// <param name="changes">The changes, by key.</param>
    /// <returns>The new snapshot.</returns>
    /// <exception cref="SettingsValidationException">The update is invalid. Nothing was saved.</exception>
    PufferfinConfigurationSnapshot Update(IReadOnlyDictionary<string, JsonElement> changes);
}
