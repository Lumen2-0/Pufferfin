namespace Pufferfin.Catalog.Configuration;

/// <summary>
/// Supplies the current catalog settings.
/// </summary>
/// <remarks>
/// The settings are read on every call so a change made in the persisted configuration store
/// takes effect without a restart. The store implementation replaces the environment based default.
/// </remarks>
public interface ICatalogSettingsProvider
{
    /// <summary>
    /// Gets the current settings.
    /// </summary>
    /// <returns>The settings.</returns>
    CatalogSettings GetSettings();
}
