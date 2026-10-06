using System;

namespace Pufferfin.Server.Configuration;

/// <summary>
/// Connection settings of a service reached by URL with an optional API key.
/// Deliberately not a record: a generated <c>ToString</c> would print the key.
/// </summary>
public sealed class ServiceSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceSettings"/> class.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="baseUrl">The base URL, when the service is reached by URL.</param>
    /// <param name="apiKey">The API key, when the service uses one.</param>
    /// <param name="username">The user name, when the service uses one.</param>
    /// <param name="password">The password, when the service uses one.</param>
    public ServiceSettings(PufferfinService service, Uri? baseUrl, string? apiKey = null, string? username = null, string? password = null)
    {
        Service = service;
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        Username = username;
        Password = password;
    }

    /// <summary>
    /// Gets the service.
    /// </summary>
    public PufferfinService Service { get; }

    /// <summary>
    /// Gets the base URL, without a trailing slash.
    /// </summary>
    public Uri? BaseUrl { get; }

    /// <summary>
    /// Gets the API key. Secret.
    /// </summary>
    public string? ApiKey { get; }

    /// <summary>
    /// Gets the user name.
    /// </summary>
    public string? Username { get; }

    /// <summary>
    /// Gets the password. Secret.
    /// </summary>
    public string? Password { get; }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(ServiceSettings)} {{ Service = {Service} }}";
}
