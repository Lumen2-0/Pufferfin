using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Pufferfin.Server.Requests.Integration;

/// <summary>
/// <see cref="IMediaLibrary"/> backed by the Jellyfin library manager and library monitor.
/// </summary>
public sealed class JellyfinMediaLibrary : IMediaLibrary
{
    /// <summary>
    /// The minimum time between two full library scans asked by Pufferfin.
    /// </summary>
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);

    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JellyfinMediaLibrary> _logger;
    private long _lastScanTicks = long.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinMediaLibrary"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="libraryMonitor">The library monitor.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public JellyfinMediaLibrary(ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, TimeProvider timeProvider, ILogger<JellyfinMediaLibrary> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool HasMovie(int tmdbId)
        => _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            HasAnyProviderIds = new Dictionary<string, string[]> { [MetadataProvider.Tmdb.ToString()] = [Id(tmdbId)] },
            IsVirtualItem = false,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        }).Count > 0;

    /// <inheritdoc />
    public bool HasEpisodes(int tmdbId, int? tvdbId, IReadOnlyCollection<RequestEpisode> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        var providerIds = new Dictionary<string, string[]> { [MetadataProvider.Tmdb.ToString()] = [Id(tmdbId)] };
        if (tvdbId is int tvdb)
        {
            providerIds[MetadataProvider.Tvdb.ToString()] = [Id(tvdb)];
        }

        var series = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            HasAnyProviderIds = providerIds,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        });
        if (series.Count == 0)
        {
            return false;
        }

        var present = new HashSet<(int Season, int Episode)>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = series.Select(s => s.Id).ToArray(),
            IsVirtualItem = false,
            Recursive = true,
            DtoOptions = new DtoOptions(false)
        }))
        {
            if (item.ParentIndexNumber is int season && item.IndexNumber is int first)
            {
                // A file holding several episodes covers each of them.
                for (var episode = first; episode <= ((item as Episode)?.IndexNumberEnd ?? first); episode++)
                {
                    present.Add((season, episode));
                }
            }
        }

        return episodes.All(e => present.Contains((e.SeasonNumber, e.EpisodeNumber)));
    }

    /// <inheritdoc />
    public void Refresh(string? path)
    {
        if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
        {
            _libraryMonitor.ReportFileSystemChanged(path);
            return;
        }

        // The *arr see the disk under other paths than this server: scan the library, not too often.
        var now = _timeProvider.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastScanTicks);
        if (last != long.MinValue && now - last < ScanInterval.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastScanTicks, now, last) != last)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _libraryManager.ValidateMediaLibrary(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A failed scan must not take the server down; the next import asks again.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogWarning(ex, "The library scan asked by Pufferfin failed");
            }
        });
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
}
