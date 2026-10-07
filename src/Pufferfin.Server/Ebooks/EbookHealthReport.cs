using System;
using System.Collections.Generic;
using System.Linq;
using Pufferfin.Server.Audiobooks;

namespace Pufferfin.Server.Ebooks;

/// <summary>
/// The state of the ebook setup, for the administration dashboard and the startup log.
/// </summary>
/// <param name="Enabled">Whether Shelfmark is configured. When it is not, books to read are off and nothing is checked: not an error.</param>
/// <param name="Checks">The checks, empty when books are off.</param>
/// <param name="CheckedAt">When the checks ran.</param>
public sealed record EbookHealthReport(bool Enabled, IReadOnlyList<EbookCheck> Checks, DateTimeOffset CheckedAt)
{
    /// <summary>
    /// Gets a value indicating whether a check found a problem.
    /// </summary>
    public bool HasProblems => Checks.Any(c => c.Status == AudiobookCheckStatus.Problem);
}
