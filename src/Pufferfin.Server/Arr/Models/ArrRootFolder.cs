namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// A root folder of Radarr or Sonarr.
/// </summary>
public sealed class ArrRootFolder
{
    /// <summary>Gets or sets the id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the path, as the service sees it.</summary>
    public string? Path { get; set; }
}
