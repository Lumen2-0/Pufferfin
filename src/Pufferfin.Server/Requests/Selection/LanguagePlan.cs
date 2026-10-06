using System;
using System.Collections.Generic;
using System.Linq;

namespace Pufferfin.Server.Requests.Selection;

/// <summary>
/// The order in which audio languages are tried: the requested language, the preferred languages, then the
/// original version, which always closes the list.
/// </summary>
public static class LanguagePlan
{
    /// <summary>
    /// Builds the plan of a request.
    /// </summary>
    /// <param name="requested">The requested language, or <see cref="MediaRequest.OriginalLanguage"/> for the original version only.</param>
    /// <param name="preferred">The preferred languages of the user, else of the server, in order.</param>
    /// <returns>The candidates, in order, ending with the original version.</returns>
    public static IReadOnlyList<LanguageCandidate> Build(string requested, IReadOnlyList<string> preferred)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(preferred);
        if (requested == MediaRequest.OriginalLanguage)
        {
            return [LanguageCandidate.Original];
        }

        return preferred
            .Prepend(requested)
            .Where(code => !string.IsNullOrWhiteSpace(code) && code != MediaRequest.OriginalLanguage)
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => new LanguageCandidate(code))
            .Append(LanguageCandidate.Original)
            .ToList();
    }
}
