using System.Collections.Generic;

namespace Pufferfin.Server.Arr.Models;

/// <summary>
/// A message attached to a queue item.
/// </summary>
public sealed class ArrStatusMessage
{
    /// <summary>Gets or sets the title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the messages.</summary>
    public IList<string> Messages { get; set; } = [];
}
