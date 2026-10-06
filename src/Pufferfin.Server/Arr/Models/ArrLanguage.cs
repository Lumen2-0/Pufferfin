namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// A language as Radarr and Sonarr name it.
/// </summary>
public sealed class ArrLanguage
{
    /// <summary>Gets or sets the id. <c>-1</c> is "Any", <c>-2</c> is "Original".</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the English name.</summary>
    public string? Name { get; set; }
}
