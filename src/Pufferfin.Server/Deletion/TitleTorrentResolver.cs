using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests.Integration;
using Pufferfin.Server.Seeding;
using Pufferfin.Server.Storage;

namespace Pufferfin.Server.Deletion;

/// <summary>
/// Links a title of the library to the torrents it came from: through the history of Radarr or Sonarr for films and
/// series, by inode for audiobooks.
/// </summary>
public sealed class TitleTorrentResolver
{
    private readonly ArrClient _arr;
    private readonly QBittorrentClient _qBittorrent;
    private readonly ITitleMetadata _metadata;
    private readonly IFileLinks _links;
    private readonly ILogger<TitleTorrentResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleTorrentResolver"/> class.
    /// </summary>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="metadata">The TMDB metadata, for the TVDB id of a series the library does not know.</param>
    /// <param name="links">The inodes and hard links.</param>
    /// <param name="logger">The logger.</param>
    public TitleTorrentResolver(ArrClient arr, QBittorrentClient qBittorrent, ITitleMetadata metadata, IFileLinks links, ILogger<TitleTorrentResolver> logger)
    {
        _arr = arr;
        _qBittorrent = qBittorrent;
        _metadata = metadata;
        _links = links;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a title.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved title.</returns>
    /// <exception cref="ArrApiException">Radarr, Sonarr or qBittorrent failed.</exception>
    public Task<ResolvedTitle> ResolveAsync(LibraryTitle title, PufferfinSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(settings);
        return title.Kind switch
        {
            LibraryTitleKind.Movie => ResolveMovieAsync(title, settings.GetService(PufferfinService.Radarr)!, cancellationToken),
            LibraryTitleKind.Audiobook => ResolveAudiobookAsync(title, settings, cancellationToken),
            _ => ResolveSeriesAsync(title, settings.GetService(PufferfinService.Sonarr)!, cancellationToken)
        };
    }

    private static bool HasDownload(ArrHistoryRecord record) => !string.IsNullOrEmpty(record.DownloadId);

    private async Task<ResolvedTitle> ResolveMovieAsync(LibraryTitle title, ServiceSettings radarr, CancellationToken cancellationToken)
    {
        if (title.TmdbId is not int tmdbId || await _arr.FindMovieAsync(radarr, tmdbId, cancellationToken).ConfigureAwait(false) is not { } movie)
        {
            return new ResolvedTitle(title, null, [], [], [], []);
        }

        var history = await _arr.GetMovieHistoryAsync(radarr, movie.Id, cancellationToken).ConfigureAwait(false);
        var hashes = history.Where(HasDownload).Select(h => h.DownloadId!.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        return new ResolvedTitle(title, movie.Id, [], [], hashes, []);
    }

    private async Task<ResolvedTitle> ResolveSeriesAsync(LibraryTitle title, ServiceSettings sonarr, CancellationToken cancellationToken)
    {
        var tvdbId = title.TvdbId;
        if (tvdbId is null && title.TmdbId is int tmdbId)
        {
            try
            {
                tvdbId = (await _metadata.GetSeriesAsync(tmdbId, cancellationToken).ConfigureAwait(false))?.TvdbId;
            }
            catch (Pufferfin.Catalog.Http.CatalogSourceException)
            {
                tvdbId = null;
            }
        }

        if (tvdbId is not int tvdb || await _arr.FindSeriesAsync(sonarr, tvdb, cancellationToken).ConfigureAwait(false) is not { } series)
        {
            return new ResolvedTitle(title, null, [], [], [], []);
        }

        var episodes = await _arr.GetEpisodesAsync(sonarr, series.Id, cancellationToken).ConfigureAwait(false);
        var deleted = title.Kind switch
        {
            LibraryTitleKind.Series => episodes,
            LibraryTitleKind.Season => episodes.Where(e => e.SeasonNumber == title.SeasonNumber).ToList(),
            _ => episodes.Where(e => e.SeasonNumber == title.SeasonNumber && e.EpisodeNumber == title.EpisodeNumber).ToList()
        };
        var deletedIds = deleted.Select(e => e.Id).ToHashSet();
        var withFile = episodes.Where(e => e.HasFile).Select(e => e.Id).ToHashSet();

        // A torrent (a season pack, a complete series) may cover more episodes than the ones deleted.
        var history = await _arr.GetSeriesHistoryAsync(sonarr, series.Id, cancellationToken).ConfigureAwait(false);
        var releasable = new List<string>();
        var shared = new List<string>();
        foreach (var download in history.Where(HasDownload).GroupBy(h => h.DownloadId!.ToLowerInvariant(), StringComparer.Ordinal))
        {
            var covered = download.Select(h => h.EpisodeId).OfType<int>().ToHashSet();
            if (!covered.Overlaps(deletedIds))
            {
                continue;
            }

            if (covered.All(id => deletedIds.Contains(id) || !withFile.Contains(id)))
            {
                releasable.Add(download.Key);
            }
            else
            {
                shared.Add(download.Key);
            }
        }

        return new ResolvedTitle(
            title,
            series.Id,
            deleted.Select(e => e.Id).ToList(),
            deleted.Where(e => e.EpisodeFileId > 0).Select(e => e.EpisodeFileId).Distinct().ToList(),
            releasable,
            shared);
    }

    private async Task<ResolvedTitle> ResolveAudiobookAsync(LibraryTitle title, PufferfinSettings settings, CancellationToken cancellationToken)
    {
        var deleted = title.FilePaths.Select(_links.GetIdentity).OfType<FileIdentity>().ToList();
        if (deleted.Count == 0 || settings.GetService(PufferfinService.QBittorrent) is not { } qBittorrent)
        {
            return new ResolvedTitle(title, null, [], [], [], []);
        }

        var paths = StoragePaths.From(settings);
        var policy = SeedPolicy.From(settings);
        var torrents = (await _qBittorrent.GetAllTorrentsAsync(qBittorrent, cancellationToken).ConfigureAwait(false))
            .Where(t => !string.IsNullOrEmpty(t.Hash) && !string.IsNullOrEmpty(t.SavePath))
            .Where(t => string.Equals(t.Category, MediaLayout.AudiobooksCategory, StringComparison.OrdinalIgnoreCase)
                || (policy.Manages(t.Category)
                    && !string.Equals(t.Category, MediaLayout.RadarrCategory, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(t.Category, MediaLayout.SonarrCategory, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var candidates = new List<(string Hash, IReadOnlyList<FileIdentity?> Files)>();
        foreach (var torrent in torrents)
        {
            var files = await _qBittorrent.GetFilesAsync(qBittorrent, torrent.Hash!, cancellationToken).ConfigureAwait(false);
            candidates.Add((
                torrent.Hash!.ToLowerInvariant(),
                files.Where(f => f.Priority > 0 && !string.IsNullOrEmpty(f.Name))
                    .Select(f => _links.GetIdentity(paths.ToLocalPath(Path.Join(torrent.SavePath, f.Name))))
                    .ToList()));
        }

        var matches = InodeTorrentMatcher.Match(deleted, candidates);
        _logger.LogDebug("Audiobook {Title}: {Count} torrents matched by inode", title.Title, matches.Count);
        return new ResolvedTitle(
            title,
            null,
            [],
            [],
            matches.Where(m => m.FullyCovered).Select(m => m.Hash).ToList(),
            matches.Where(m => !m.FullyCovered).Select(m => m.Hash).ToList());
    }
}
