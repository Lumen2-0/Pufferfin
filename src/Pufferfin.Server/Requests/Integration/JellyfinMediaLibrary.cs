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
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
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
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JellyfinMediaLibrary> _logger;
    private long _lastScanTicks = long.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinMediaLibrary"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="libraryMonitor">The library monitor.</param>
    /// <param name="providerManager">The provider manager, to read the metadata of a film again.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public JellyfinMediaLibrary(
        ILibraryManager libraryManager,
        ILibraryMonitor libraryMonitor,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<JellyfinMediaLibrary> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
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

    /// <inheritdoc />
    public bool ClaimMovie(int tmdbId, string? arrFolder)
    {
        var name = Path.GetFileName(arrFolder?.TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var segment = Path.DirectorySeparatorChar + name + Path.DirectorySeparatorChar;
        var item = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsVirtualItem = false,
            Recursive = true,
            // With the provider ids: without them every film would look like the wrong one and be rewritten
            DtoOptions = new DtoOptions(true)
        }).FirstOrDefault(i => i.Path?.Contains(segment, StringComparison.OrdinalIgnoreCase) == true);
        if (item is null)
        {
            return false;
        }

        var tmdb = Id(tmdbId);
        if (!string.Equals(item.GetProviderId(MetadataProvider.Tmdb), tmdb, StringComparison.Ordinal))
        {
            _logger.LogInformation("The library took the film in {Folder} for another one: it gets back TMDB id {TmdbId}", name, tmdbId);

            // The other ids belong to the wrong film: only the TMDB id is kept, the rest comes back with the metadata
            item.ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MetadataProvider.Tmdb.ToString()] = tmdb };
            _ = Task.Run(async () =>
            {
                try
                {
                    await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
                    _providerManager.QueueRefresh(
                        item.Id,
                        new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                        {
                            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                            ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                            ReplaceAllMetadata = true,
                            ReplaceAllImages = true
                        },
                        RefreshPriority.High);
                }
#pragma warning disable CA1031 // A failed identification must not take the server down; the film is in the library anyway.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _logger.LogWarning(ex, "The film in {Folder} could not get its TMDB id back", name);
                }
            });
        }

        return true;
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
}
