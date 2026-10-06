using System;
using System.Collections.Generic;
using System.Linq;

namespace Pufferfin.Server.Audiobooks;

/// <summary>
/// The state of the audiobook setup, for the administration dashboard and the startup log.
/// </summary>
/// <param name="Enabled">Whether Shelfmark is configured. When it is not, audiobooks are off and nothing is checked: not an error.</param>
/// <param name="Checks">The checks, empty when audiobooks are off.</param>
/// <param name="CheckedAt">When the checks ran.</param>
public sealed record AudiobookHealthReport(bool Enabled, IReadOnlyList<AudiobookCheck> Checks, DateTimeOffset CheckedAt)
{
    /// <summary>
    /// Gets a value indicating whether a check found a problem.
    /// </summary>
    public bool HasProblems => Checks.Any(c => c.Status == AudiobookCheckStatus.Problem);
}
