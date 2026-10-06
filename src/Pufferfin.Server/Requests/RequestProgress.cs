namespace Pufferfin.Server.Requests;

/// <summary>
/// The live progress of a download, read from qBittorrent and the queue of Radarr or Sonarr.
/// </summary>
/// <param name="Fraction">The progress, from 0 to 1.</param>
/// <param name="BytesPerSecond">The download speed, when qBittorrent reports it.</param>
/// <param name="EtaSeconds">The estimated time left, when known.</param>
public sealed record RequestProgress(double Fraction, long? BytesPerSecond, long? EtaSeconds);
