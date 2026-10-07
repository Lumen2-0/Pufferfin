namespace Pufferfin.Catalog.Tmdb;

/// <summary>
/// The TMDB type of a series, as filtered by the <c>with_type</c> parameter of <c>/discover/tv</c>.
/// </summary>
/// <remarks>
/// The values are the TMDB codes: do not renumber.
/// </remarks>
public enum TmdbSeriesType
{
    /// <summary>
    /// A documentary series.
    /// </summary>
    Documentary = 0,

    /// <summary>
    /// A news programme.
    /// </summary>
    News = 1,

    /// <summary>
    /// A miniseries.
    /// </summary>
    Miniseries = 2,

    /// <summary>
    /// A reality show.
    /// </summary>
    Reality = 3,

    /// <summary>
    /// A scripted series (fiction).
    /// </summary>
    Scripted = 4,

    /// <summary>
    /// A talk show.
    /// </summary>
    TalkShow = 5,

    /// <summary>
    /// A video series.
    /// </summary>
    Video = 6
}
