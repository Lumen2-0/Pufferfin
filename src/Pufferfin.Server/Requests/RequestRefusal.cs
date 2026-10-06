namespace Pufferfin.Server.Requests;

/// <summary>
/// Why a request was refused at creation.
/// </summary>
public enum RequestRefusal
{
    /// <summary>
    /// The input is invalid.
    /// </summary>
    Invalid,

    /// <summary>
    /// Radarr or Sonarr is not configured.
    /// </summary>
    ServiceDisabled,

    /// <summary>
    /// The account may not request titles.
    /// </summary>
    NotAllowed,

    /// <summary>
    /// The weekly quota of the account is reached.
    /// </summary>
    QuotaExceeded,

    /// <summary>
    /// Too many requests in a short time.
    /// </summary>
    RateLimited,

    /// <summary>
    /// TMDB does not know the title.
    /// </summary>
    TitleUnknown
}
