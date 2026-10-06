using System;

namespace Pufferfin.Catalog.Configuration;

/// <summary>
/// Reads the catalog settings from environment variables.
/// </summary>
/// <remarks>
/// Interim provider until the persisted configuration store is available: the store is the source of truth
/// and registers its own <see cref="ICatalogSettingsProvider"/>, which takes precedence over this one.
/// </remarks>
public sealed class EnvironmentCatalogSettingsProvider : ICatalogSettingsProvider
{
    private readonly Func<string, string?> _getVariable;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentCatalogSettingsProvider"/> class.
    /// </summary>
    public EnvironmentCatalogSettingsProvider()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentCatalogSettingsProvider"/> class.
    /// </summary>
    /// <param name="getVariable">Reads one variable.</param>
    public EnvironmentCatalogSettingsProvider(Func<string, string?> getVariable)
    {
        _getVariable = getVariable;
    }

    /// <inheritdoc />
    public CatalogSettings GetSettings()
    {
        var languages = CatalogSettings.SplitList(_getVariable("TMDB_LANGUAGES"));
        return new CatalogSettings
        {
            TmdbApiKey = _getVariable("TMDB_API_KEY")?.Trim(),
            TmdbLanguages = languages.Count > 0 ? languages : [CatalogSettings.DefaultTmdbLanguage],
            AudibleMarkets = CatalogSettings.SplitList(_getVariable("AUDIBLE_MARKETS"))
        };
    }
}
