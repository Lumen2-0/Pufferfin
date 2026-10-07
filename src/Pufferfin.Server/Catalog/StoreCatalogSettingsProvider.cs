using Pufferfin.Catalog.Configuration;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Catalog;

/// <summary>
/// Catalog settings read from the persisted configuration store, the source of truth.
/// </summary>
/// <remarks>
/// The environment only seeds the store (see <see cref="EnvironmentReconciler"/>): a key changed in the admin panel
/// wins over the variable that seeded it, and applies to the next catalog call without a restart.
/// </remarks>
public sealed class StoreCatalogSettingsProvider : ICatalogSettingsProvider
{
    private readonly IPufferfinConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreCatalogSettingsProvider"/> class.
    /// </summary>
    /// <param name="store">The configuration store.</param>
    public StoreCatalogSettingsProvider(IPufferfinConfigurationStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public CatalogSettings GetSettings()
    {
        var settings = _store.Current.Settings;
        return new CatalogSettings
        {
            TmdbApiKey = settings.GetService(PufferfinService.Tmdb)?.ApiKey,
            TmdbLanguages = settings.TmdbLanguages.Count > 0 ? settings.TmdbLanguages : [CatalogSettings.DefaultTmdbLanguage],
            AudibleMarkets = settings.AudibleMarkets,
            PreferredAudioLanguages = settings.PreferredAudioLanguages
        };
    }
}
