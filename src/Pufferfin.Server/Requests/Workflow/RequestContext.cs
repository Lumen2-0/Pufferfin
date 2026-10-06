using System;
using System.Collections.Generic;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Requests.Workflow;

/// <summary>
/// What a workflow step needs besides the request: the settings, the time and the languages of the user.
/// </summary>
/// <param name="Settings">The current settings.</param>
/// <param name="Now">The current time.</param>
/// <param name="PreferredLanguages">The preferred audio languages of the request owner, else of the server, in order.</param>
public sealed record RequestContext(PufferfinSettings Settings, DateTimeOffset Now, IReadOnlyList<string> PreferredLanguages)
{
    /// <summary>
    /// Gets a value indicating whether a release subtitled only in the wanted language is refused (the default).
    /// </summary>
    public bool RejectSubtitledOnly => Settings.RejectSubtitledOnly ?? true;
}
