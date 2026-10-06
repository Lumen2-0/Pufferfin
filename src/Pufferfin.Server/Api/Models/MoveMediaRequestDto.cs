using System.ComponentModel.DataAnnotations;

namespace Pufferfin.Server.Api.Models;

/// <summary>
/// A new place in the queue.
/// </summary>
public sealed class MoveMediaRequestDto
{
    /// <summary>Gets or sets the zero based place among the requests waiting for approval or for a slot.</summary>
    [Range(0, int.MaxValue)]
    public int Index { get; set; }
}
