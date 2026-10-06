namespace Pufferfin.Server.Users;

/// <summary>
/// The Pufferfin settings a user manages for themselves. Every field is optional.
/// </summary>
public sealed record PufferfinUserSettings
{
    /// <summary>
    /// Gets the settings of a user who never saved any.
    /// </summary>
    public static PufferfinUserSettings Empty { get; } = new();

    /// <summary>
    /// Gets the Letterboxd username whose diary feeds the user's "Recommended for you" row.
    /// </summary>
    public string? LetterboxdUsername { get; init; }
}
