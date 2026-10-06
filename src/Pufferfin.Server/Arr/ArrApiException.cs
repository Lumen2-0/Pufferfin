using System;
using System.Collections.Generic;
using System.Globalization;
using Pufferfin.Server.Configuration;

namespace Pufferfin.Server.Arr;

/// <summary>
/// A call to a service failed.
/// </summary>
/// <remarks>
/// The message is written here and never quotes a URL or a key: it is safe to log.
/// The remote messages have every secret of the service removed.
/// </remarks>
public sealed class ArrApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrApiException"/> class.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="failure">Why the call failed.</param>
    /// <param name="statusCode">The HTTP status code, when the service answered.</param>
    /// <param name="remoteMessages">The messages the service gave, already redacted.</param>
    public ArrApiException(PufferfinService service, ArrFailure failure, int? statusCode = null, IReadOnlyList<string>? remoteMessages = null)
        : base(Describe(service, failure, statusCode))
    {
        Service = service;
        Failure = failure;
        StatusCode = statusCode;
        RemoteMessages = remoteMessages ?? [];
    }

    /// <summary>
    /// Gets the service.
    /// </summary>
    public PufferfinService Service { get; }

    /// <summary>
    /// Gets why the call failed.
    /// </summary>
    public ArrFailure Failure { get; }

    /// <summary>
    /// Gets the HTTP status code, when the service answered.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Gets the messages the service gave, with every secret of the service removed. Empty when none.
    /// </summary>
    public IReadOnlyList<string> RemoteMessages { get; }

    private static string Describe(PufferfinService service, ArrFailure failure, int? statusCode) => failure switch
    {
        ArrFailure.Unreachable => $"{service} could not be reached.",
        ArrFailure.TimedOut => $"{service} did not answer in time.",
        ArrFailure.KeyRejected => $"{service} refused the API key or the credentials.",
        ArrFailure.NotFound => $"{service} does not know this resource.",
        ArrFailure.Rejected => string.Create(CultureInfo.InvariantCulture, $"{service} refused the request (HTTP {statusCode})."),
        ArrFailure.ServerError => string.Create(CultureInfo.InvariantCulture, $"{service} failed while handling the request (HTTP {statusCode})."),
        _ => $"{service} answered with something Pufferfin could not read."
    };
}
