using System;

namespace Pufferfin.Server.Users;

/// <summary>
/// The persisted per user Pufferfin settings.
/// </summary>
public interface IPufferfinUserSettingsStore
{
    /// <summary>
    /// Gets the settings of a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The settings, <see cref="PufferfinUserSettings.Empty"/> when the user never saved any.</returns>
    PufferfinUserSettings Get(Guid userId);

    /// <summary>
    /// Validates and saves the settings of a user.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="settings">The new settings. Blank values are stored as unset.</param>
    /// <returns>The saved settings.</returns>
    /// <exception cref="Configuration.SettingsValidationException">A value is invalid. Nothing was saved.</exception>
    PufferfinUserSettings Set(Guid userId, PufferfinUserSettings settings);
}
