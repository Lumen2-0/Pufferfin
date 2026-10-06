using System;
using System.Collections.Generic;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// Thrown when a configuration update is rejected. Messages name keys, never values.
/// </summary>
public sealed class SettingsValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsValidationException"/> class.
    /// </summary>
    /// <param name="errors">The errors, by key.</param>
    public SettingsValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("The configuration update was rejected.")
    {
        Errors = errors;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsValidationException"/> class.
    /// </summary>
    public SettingsValidationException()
        : this(new Dictionary<string, string[]>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsValidationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public SettingsValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsValidationException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SettingsValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new Dictionary<string, string[]>();
    }

    /// <summary>
    /// Gets the errors, by key.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
