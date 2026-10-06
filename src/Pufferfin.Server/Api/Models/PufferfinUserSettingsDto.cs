using Pufferfin.Server.Users;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// The Pufferfin settings of the current user. Every field is optional.
/// </summary>
public sealed class PufferfinUserSettingsDto
{
    /// <summary>
    /// Gets or sets the Letterboxd username whose diary feeds the "Recommended for you" row. Empty or <c>null</c> unlinks it.
    /// </summary>
    public string? LetterboxdUsername { get; set; }

    /// <summary>
    /// Builds the DTO from stored settings.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The DTO.</returns>
    public static PufferfinUserSettingsDto From(PufferfinUserSettings settings)
        => new() { LetterboxdUsername = settings.LetterboxdUsername };
}
