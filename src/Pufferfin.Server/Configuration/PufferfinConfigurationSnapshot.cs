using System;
using System.Collections.Generic;
using System.Linq;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// An immutable snapshot of the configuration store.
/// </summary>
public sealed class PufferfinConfigurationSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PufferfinConfigurationSnapshot"/> class.
    /// </summary>
    /// <param name="states">The state of every known setting.</param>
    public PufferfinConfigurationSnapshot(IReadOnlyList<SettingState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        States = states;
        StatesByKey = states.ToDictionary(s => s.Definition.Key, StringComparer.Ordinal);
        Settings = new PufferfinSettings(StatesByKey);
    }

    /// <summary>
    /// Gets the state of every known setting, in catalog order.
    /// </summary>
    public IReadOnlyList<SettingState> States { get; }

    /// <summary>
    /// Gets the state of every known setting, by key.
    /// </summary>
    public IReadOnlyDictionary<string, SettingState> StatesByKey { get; }

    /// <summary>
    /// Gets the strongly typed settings.
    /// </summary>
    public PufferfinSettings Settings { get; }
}
