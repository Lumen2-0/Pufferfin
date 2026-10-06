namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// A quality definition.
/// </summary>
public sealed class ArrQuality
{
    /// <summary>Gets or sets the id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the name, for example <c>Bluray-1080p</c>.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the vertical resolution, <c>0</c> when unknown.</summary>
    public int Resolution { get; set; }
}
