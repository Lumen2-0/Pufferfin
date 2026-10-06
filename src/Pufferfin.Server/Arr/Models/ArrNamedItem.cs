namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// A named item with an id: quality profile, custom format, download client, notification...
/// </summary>
public sealed class ArrNamedItem
{
    /// <summary>Gets or sets the id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the implementation, for provider items.</summary>
    public string? Implementation { get; set; }
}
