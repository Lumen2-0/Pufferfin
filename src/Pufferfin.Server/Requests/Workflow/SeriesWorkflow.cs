using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Arr.Models;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests.Integration;
using Pufferfin.Server.Requests.Selection;

namespace Pufferfin.Server.Requests.Workflow;

/// <summary>
/// Seasons and episodes, through Sonarr.
/// </summary>
/// <remarks>
/// <para>
/// Sonarr works with TVDB ids: the TMDB id is converted through TMDB. Episodes are matched by number, else by
/// air date one day either side.
/// </para>
/// <para>
/// One episode: only that episode is monitored, and only a release of that episode is grabbed. A season pack is
/// never taken for one episode, even when it is the only release.
/// </para>
/// <para>
/// A season: the season is monitored, then a season pack is taken in the first language that has one. Without a
/// pack, Sonarr searches the episodes one by one with the profile of the first language that has episode releases.
/// Episodes already downloaded or on their way are never searched again.
/// </para>
/// </remarks>
public sealed class SeriesWorkflow : ArrWorkflowBase
{
    /// <summary>
    /// How long Sonarr may take to list the episodes of a series it just added.
    /// </summary>
    public static readonly TimeSpan EpisodeListGracePeriod = TimeSpan.FromMinutes(10);

    private readonly ITitleMetadata _metadata;
    private readonly IMediaLibrary _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesWorkflow"/> class.
    /// </summary>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="metadata">The TMDB metadata.</param>
    /// <param name="library">The Jellyfin library.</param>
    /// <param name="logger">The logger.</param>
    public SeriesWorkflow(ArrClient arr, QBittorrentClient qBittorrent, ITitleMetadata metadata, IMediaLibrary library, ILogger<SeriesWorkflow> logger)
        : base(PufferfinService.Sonarr, arr, qBittorrent, logger)
    {
        _metadata = metadata;
        _library = library;
    }

    /// <inheritdoc />
    public override bool Handles(RequestKind kind) => kind is RequestKind.Season or RequestKind.Episode;

    /// <inheritdoc />
    protected override async Task<MediaRequest> SearchCoreAsync(MediaRequest request, ServiceSettings service, RequestContext context, CancellationToken cancellationToken)
    {
        var metadata = await _metadata.GetSeriesAsync(request.TmdbId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return request.Fail(context.Now, RequestProblemCode.TitleUnknown, "TMDB does not know this series.");
        }

        if (metadata.TvdbId is not int tvdbId)
        {
            return request.Fail(context.Now, RequestProblemCode.TvdbIdMissing, "TMDB gives no TVDB id for this series, and Sonarr needs one.");
        }

        var plan = LanguagePlan.Build(request.Language, context.PreferredLanguages);
        var series = await Arr.FindSeriesAsync(service, tvdbId, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            var (added, failed) = await AddAsync(request, service, metadata, tvdbId, plan[0], context, cancellationToken).ConfigureAwait(false);
            if (added is null)
            {
                return failed!;
            }

            series = added;
        }

        request = request with { ArrId = series.Id, TvdbId = tvdbId };
        var episodes = await Arr.GetEpisodesAsync(service, series.Id, cancellationToken).ConfigureAwait(false);
        if (episodes.Count == 0)
        {
            // Sonarr lists the episodes of a new series a moment after adding it.
            return context.Now - request.UpdatedAt < EpisodeListGracePeriod
                ? request with { NextAttemptAt = context.Now + TimeSpan.FromSeconds(30) }
                : request.Fail(context.Now, RequestProblemCode.EpisodeNotFound, "Sonarr lists no episode for this series.");
        }

        return request.Kind == RequestKind.Episode
            ? await SearchEpisodeAsync(request, service, series, episodes, plan, metadata, context, cancellationToken).ConfigureAwait(false)
            : await SearchSeasonAsync(request, service, series, episodes, plan, metadata, context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<MediaRequest> TrackCoreAsync(MediaRequest request, ServiceSettings service, RequestContext context, CancellationToken cancellationToken)
    {
        if (request.Episodes.Count == 0)
        {
            return request;
        }

        if (_library.HasEpisodes(request.TmdbId, request.TvdbId, request.Episodes))
        {
            return request.To(RequestState.Available, context.Now);
        }

        var ids = request.Episodes.Select(e => e.EpisodeId).ToHashSet();
        var queue = (await Arr.GetSeriesQueueAsync(service, request.ArrId!.Value, cancellationToken).ConfigureAwait(false))
            .Where(q => q.EpisodeId is int id && ids.Contains(id))
            .ToList();
        if (queue.Count > 0)
        {
            return await FollowQueueAsync(request, queue, context, cancellationToken).ConfigureAwait(false);
        }

        var episodes = (await Arr.GetEpisodesAsync(service, request.ArrId.Value, cancellationToken).ConfigureAwait(false))
            .Where(e => ids.Contains(e.Id))
            .ToList();
        if (episodes.Count > 0 && episodes.All(e => e.HasFile))
        {
            var series = await Arr.GetSeriesObjectAsync(service, request.ArrId.Value, cancellationToken).ConfigureAwait(false);
            _library.Refresh(series["path"]?.GetValue<string>());
            return request.To(RequestState.Importing, context.Now);
        }

        var history = await HistoryAsync(service, request.ArrId.Value, request.Episodes.Select(e => e.SeasonNumber).Distinct(), cancellationToken).ConfigureAwait(false);
        var missing = episodes.Where(e => !e.HasFile).ToList();
        if (missing.All(e => DuplicateGuard.Check(false, [], history.Where(h => h.EpisodeId == e.Id)).Covered))
        {
            return request.State == RequestState.WaitingForSources ? request.To(RequestState.Downloading, context.Now) : request;
        }

        return NothingInFlight(request, LastFailure(request, history.Where(h => h.EpisodeId is int id && ids.Contains(id))), context);
    }

    private async Task<MediaRequest> SearchEpisodeAsync(
        MediaRequest request,
        ServiceSettings service,
        ArrSeries series,
        IReadOnlyList<ArrEpisode> episodes,
        IReadOnlyList<LanguageCandidate> plan,
        TitleMetadata metadata,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var match = EpisodeMatcher.Match(episodes, request.SeasonNumber!.Value, request.EpisodeNumber!.Value, request.AirDate);
        if (match is null)
        {
            var aired = request.AirDate is { } date ? " nor one aired on " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", one day either side" : string.Empty;
            return request.Fail(
                context.Now,
                RequestProblemCode.EpisodeNotFound,
                string.Create(CultureInfo.InvariantCulture, $"Sonarr has no episode S{request.SeasonNumber:00}E{request.EpisodeNumber:00}{aired}."));
        }

        request = request with { Episodes = [new RequestEpisode(match.Id, match.SeasonNumber, match.EpisodeNumber)] };
        if (match.HasFile)
        {
            return request.To(RequestState.Importing, context.Now);
        }

        if ((await CoveredEpisodesAsync(service, series.Id, [match], cancellationToken).ConfigureAwait(false)).Contains(match.Id))
        {
            return request.To(RequestState.Downloading, context.Now);
        }

        // Monitor this episode only: monitoring the season would let Sonarr grab the others.
        await Arr.MonitorEpisodesAsync(service, [match.Id], cancellationToken).ConfigureAwait(false);
        await EnsureMonitoredAsync(service, series.Id, null, null, cancellationToken).ConfigureAwait(false);

        var releases = await Arr.SearchEpisodeReleasesAsync(service, match.Id, cancellationToken).ConfigureAwait(false);
        var selection = ReleaseSelector.Select(
            releases,
            plan,
            metadata.OriginalLanguage,
            context.RejectSubtitledOnly,
            new SelectionScope(SelectionMode.Episode, match.SeasonNumber, match.EpisodeNumber));
        if (selection.Release is not { } release)
        {
            return await NoReleaseAsync(request, service, selection.Problem!, context, cancellationToken).ConfigureAwait(false);
        }

        if ((await CoveredEpisodesAsync(service, series.Id, [match], cancellationToken).ConfigureAwait(false)).Contains(match.Id))
        {
            return request.To(RequestState.Downloading, context.Now);
        }

        await EnsureMonitoredAsync(service, series.Id, null, selection.Language, cancellationToken).ConfigureAwait(false);
        await Arr.GrabReleaseAsync(service, release, cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("Request {RequestId}: grabbed a release for one episode of series {TmdbId} in {Language}", request.Id, request.TmdbId, selection.Language!.Code);
        return Grabbed(request, release.Title, selection.Language, context);
    }

    private async Task<MediaRequest> SearchSeasonAsync(
        MediaRequest request,
        ServiceSettings service,
        ArrSeries series,
        IReadOnlyList<ArrEpisode> episodes,
        IReadOnlyList<LanguageCandidate> plan,
        TitleMetadata metadata,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var season = request.SeasonNumber!.Value;
        var inSeason = episodes.Where(e => e.SeasonNumber == season).ToList();
        if (inSeason.Count == 0)
        {
            return request.Fail(context.Now, RequestProblemCode.EpisodeNotFound, string.Create(CultureInfo.InvariantCulture, $"Sonarr has no season {season}."));
        }

        await Arr.MonitorEpisodesAsync(service, inSeason.Select(e => e.Id).ToList(), cancellationToken).ConfigureAwait(false);
        await EnsureMonitoredAsync(service, series.Id, season, null, cancellationToken).ConfigureAwait(false);

        var aired = inSeason.Where(e => e.AirDateUtc is { } airDate && airDate <= context.Now).ToList();
        request = request with { Episodes = aired.Select(e => new RequestEpisode(e.Id, e.SeasonNumber, e.EpisodeNumber)).ToList() };
        if (aired.Count == 0)
        {
            return request.Wait(context.Now, new RequestProblem(RequestProblemCode.NoRelease, "The season has not aired yet. It stays monitored."));
        }

        var covered = await CoveredEpisodesAsync(service, series.Id, aired, cancellationToken).ConfigureAwait(false);
        var missing = aired.Where(e => !e.HasFile && !covered.Contains(e.Id)).ToList();
        if (missing.Count == 0)
        {
            return request.To(aired.All(e => e.HasFile) ? RequestState.Importing : RequestState.Downloading, context.Now);
        }

        if (missing.Count < aired.Count)
        {
            // Part of the season is there or on its way: a pack would download it twice. Search the rest one by one.
            await SearchEpisodesAsync(service, missing, cancellationToken).ConfigureAwait(false);
            return Grabbed(request, null, null, context);
        }

        var releases = await Arr.SearchSeasonReleasesAsync(service, series.Id, season, cancellationToken).ConfigureAwait(false);
        var pack = ReleaseSelector.Select(releases, plan, metadata.OriginalLanguage, context.RejectSubtitledOnly, new SelectionScope(SelectionMode.SeasonPack, season));
        covered = await CoveredEpisodesAsync(service, series.Id, aired, cancellationToken).ConfigureAwait(false);
        if (covered.Count > 0)
        {
            // Sonarr grabbed some episodes meanwhile: no pack on top of them.
            await SearchEpisodesAsync(service, aired.Where(e => !covered.Contains(e.Id)).ToList(), cancellationToken).ConfigureAwait(false);
            return Grabbed(request, null, null, context);
        }

        if (pack.Release is { } release)
        {
            await EnsureMonitoredAsync(service, series.Id, season, pack.Language, cancellationToken).ConfigureAwait(false);
            await Arr.GrabReleaseAsync(service, release, cancellationToken).ConfigureAwait(false);
            Logger.LogInformation("Request {RequestId}: grabbed a season pack of series {TmdbId} in {Language}", request.Id, request.TmdbId, pack.Language!.Code);
            return Grabbed(request, release.Title, pack.Language, context);
        }

        // No pack: the first language that has episode releases, and Sonarr takes the episodes one by one in it.
        var language = plan.FirstOrDefault(candidate => releases.Any(r => !r.FullSeason
            && r.SeasonNumber == season
            && ReleaseSelector.Select([r], [candidate], metadata.OriginalLanguage, context.RejectSubtitledOnly, new SelectionScope(SelectionMode.Movie)).Release is not null));
        if (language is null)
        {
            var problem = ReleaseSelector.Select(releases, plan, metadata.OriginalLanguage, context.RejectSubtitledOnly, new SelectionScope(SelectionMode.Movie)).Problem
                ?? new RequestProblem(RequestProblemCode.NoRelease, "No release was found yet.");
            return await NoReleaseAsync(request, service, problem, context, cancellationToken).ConfigureAwait(false);
        }

        await EnsureMonitoredAsync(service, series.Id, season, language, cancellationToken).ConfigureAwait(false);
        await SearchEpisodesAsync(service, missing, cancellationToken).ConfigureAwait(false);
        return Grabbed(request, null, language, context);
    }

    private static MediaRequest Grabbed(MediaRequest request, string? releaseTitle, LanguageCandidate? language, RequestContext context)
        => request.To(RequestState.Downloading, context.Now) with
        {
            ChosenLanguage = language?.Code ?? request.ChosenLanguage,
            ReleaseTitle = releaseTitle ?? request.ReleaseTitle,
            GrabbedAt = context.Now,
            Attempts = request.Attempts + 1,
            Progress = new RequestProgress(0, null, null)
        };

    private Task SearchEpisodesAsync(ServiceSettings service, IReadOnlyList<ArrEpisode> episodes, CancellationToken cancellationToken)
        => Arr.SendCommandAsync(
            service,
            new JsonObject { ["name"] = "EpisodeSearch", ["episodeIds"] = new JsonArray(episodes.Select(e => (JsonNode)e.Id).ToArray()) },
            cancellationToken);

    private async Task<HashSet<int>> CoveredEpisodesAsync(ServiceSettings service, int seriesId, IReadOnlyList<ArrEpisode> episodes, CancellationToken cancellationToken)
    {
        var queue = await Arr.GetSeriesQueueAsync(service, seriesId, cancellationToken).ConfigureAwait(false);
        var history = await HistoryAsync(service, seriesId, episodes.Select(e => e.SeasonNumber).Distinct(), cancellationToken).ConfigureAwait(false);
        return episodes
            .Where(e => DuplicateGuard.Check(e.HasFile, queue.Where(q => q.EpisodeId == e.Id), history.Where(h => h.EpisodeId == e.Id)).Covered)
            .Select(e => e.Id)
            .ToHashSet();
    }

    private async Task<List<ArrHistoryRecord>> HistoryAsync(ServiceSettings service, int seriesId, IEnumerable<int> seasons, CancellationToken cancellationToken)
    {
        var history = new List<ArrHistoryRecord>();
        foreach (var season in seasons)
        {
            history.AddRange(await Arr.GetSeasonHistoryAsync(service, seriesId, season, cancellationToken).ConfigureAwait(false));
        }

        return history;
    }

    private async Task EnsureMonitoredAsync(ServiceSettings service, int seriesId, int? season, LanguageCandidate? language, CancellationToken cancellationToken)
    {
        var series = await Arr.GetSeriesObjectAsync(service, seriesId, cancellationToken).ConfigureAwait(false);
        var changed = false;
        if (series["monitored"]?.GetValue<bool>() != true)
        {
            series["monitored"] = true;
            changed = true;
        }

        if (season is int seasonNumber
            && (series["seasons"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(s => s["seasonNumber"]?.GetValue<int>() == seasonNumber) is { } entry
            && entry["monitored"]?.GetValue<bool>() != true)
        {
            entry["monitored"] = true;
            changed = true;
        }

        if (language is not null
            && series["qualityProfileId"]?.GetValue<int>() is int current
            && await AlignedProfileIdAsync(service, current, language, cancellationToken).ConfigureAwait(false) is int profileId)
        {
            series["qualityProfileId"] = profileId;
            changed = true;
        }

        if (changed)
        {
            await Arr.UpdateSeriesAsync(service, seriesId, series, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(ArrSeries? Series, MediaRequest? Failed)> AddAsync(
        MediaRequest request,
        ServiceSettings service,
        TitleMetadata metadata,
        int tvdbId,
        LanguageCandidate firstLanguage,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var folders = await Arr.GetRootFoldersAsync(service, cancellationToken).ConfigureAwait(false);
        var category = MediaLayout.Classify(metadata.GenreIds, metadata.CountryCodes, metadata.OriginalLanguage);
        if (MediaLayout.PickRootFolder(category, MediaLayout.SonarrRootFolders, folders) is not { } rootFolder)
        {
            return (null, request.Fail(context.Now, RequestProblemCode.NoRootFolder, "Sonarr has no root folder to file the series in."));
        }

        if (await ProfileIdAsync(service, firstLanguage, cancellationToken).ConfigureAwait(false) is not int profileId)
        {
            return (null, request.Fail(context.Now, RequestProblemCode.ServiceUnavailable, "Sonarr has no quality profile."));
        }

        if (await Arr.LookupSeriesAsync(service, tvdbId, cancellationToken).ConfigureAwait(false) is not { } series)
        {
            return (null, request.Fail(context.Now, RequestProblemCode.TitleUnknown, "Sonarr does not find this series on TVDB."));
        }

        series["qualityProfileId"] = profileId;
        series["rootFolderPath"] = rootFolder;
        series["monitored"] = true;
        series["seasonFolder"] = true;
        series["seriesType"] = category == MediaCategory.Anime ? "anime" : "standard";
        series["addOptions"] = new JsonObject { ["monitor"] = "none", ["searchForMissingEpisodes"] = false, ["searchForCutoffUnmetEpisodes"] = false };
        series["tags"] ??= new JsonArray();
        foreach (var season in (series["seasons"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            season["monitored"] = false;
        }

        try
        {
            return (await Arr.AddSeriesAsync(service, series, cancellationToken).ConfigureAwait(false), null);
        }
        catch (ArrApiException ex) when (ex.Failure == ArrFailure.Rejected)
        {
            // Added by someone else in the meantime.
            if (await Arr.FindSeriesAsync(service, tvdbId, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                return (existing, null);
            }

            throw;
        }
    }
}
