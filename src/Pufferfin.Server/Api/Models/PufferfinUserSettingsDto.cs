using System.Collections.Generic;
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
    /// Gets or sets the audio languages the user prefers for downloads, in order (ISO 639-1 codes). An empty list clears them,
    /// so the languages of the server apply. On update, <c>null</c> keeps the current list.
    /// </summary>
    public IReadOnlyList<string>? PreferredAudioLanguages { get; set; }

    /// <summary>
    /// Builds the DTO from stored settings.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The DTO.</returns>
    public static PufferfinUserSettingsDto From(PufferfinUserSettings settings)
        => new() { LetterboxdUsername = settings.LetterboxdUsername, PreferredAudioLanguages = settings.PreferredAudioLanguages };
}
