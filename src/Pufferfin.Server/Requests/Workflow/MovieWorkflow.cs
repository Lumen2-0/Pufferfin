using System.Collections.Generic;
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
/// Films, through Radarr.
/// </summary>
/// <remarks>
/// The search: add the film to Radarr when missing (profile of the first language tried, root folder of its
/// category), check the queue and the grab history against a double download, run an interactive search, take
/// the first release in the first language that has one (original version last), check the queue and the
/// history again (the search is slow, and Radarr may have grabbed meanwhile), then grab.
/// </remarks>
public sealed class MovieWorkflow : ArrWorkflowBase
{
    private readonly ITitleMetadata _metadata;
    private readonly IMediaLibrary _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieWorkflow"/> class.
    /// </summary>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="qBittorrent">The qBittorrent client.</param>
    /// <param name="metadata">The TMDB metadata.</param>
    /// <param name="library">The Jellyfin library.</param>
    /// <param name="logger">The logger.</param>
    public MovieWorkflow(ArrClient arr, QBittorrentClient qBittorrent, ITitleMetadata metadata, IMediaLibrary library, ILogger<MovieWorkflow> logger)
        : base(PufferfinService.Radarr, arr, qBittorrent, logger)
    {
        _metadata = metadata;
        _library = library;
    }

    /// <inheritdoc />
    public override bool Handles(RequestKind kind) => kind == RequestKind.Movie;

    /// <inheritdoc />
    protected override async Task<MediaRequest> SearchCoreAsync(MediaRequest request, ServiceSettings service, RequestContext context, CancellationToken cancellationToken)
    {
        if (_library.HasMovie(request.TmdbId))
        {
            return request.To(RequestState.Available, context.Now);
        }

        var metadata = await _metadata.GetMovieAsync(request.TmdbId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return request.Fail(context.Now, RequestProblemCode.TitleUnknown, "TMDB does not know this film.");
        }

        var plan = LanguagePlan.Build(request.Language, context.PreferredLanguages);
        var movie = await Arr.FindMovieAsync(service, request.TmdbId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            var (added, failed) = await AddAsync(request, service, metadata, plan[0], context, cancellationToken).ConfigureAwait(false);
            if (added is null)
            {
                return failed!;
            }

            movie = added;
        }

        request = request with { ArrId = movie.Id };
        if (movie.HasFile)
        {
            return request.To(RequestState.Importing, context.Now);
        }

        if ((await CoverageAsync(service, movie.Id, cancellationToken).ConfigureAwait(false)).Covered)
        {
            return request.To(RequestState.Downloading, context.Now);
        }

        var releases = await Arr.SearchMovieReleasesAsync(service, movie.Id, cancellationToken).ConfigureAwait(false);
        var selection = ReleaseSelector.Select(releases, plan, metadata.OriginalLanguage, context.RejectSubtitledOnly, SelectionScope.Movie);
        if (selection.Release is not { } release)
        {
            return await NoReleaseAsync(request, service, selection.Problem!, context, cancellationToken).ConfigureAwait(false);
        }

        // The search took a while: Radarr may have grabbed the film from its RSS feed meanwhile.
        var refreshed = await Arr.FindMovieAsync(service, request.TmdbId, cancellationToken).ConfigureAwait(false) ?? movie;
        if (refreshed.HasFile || (await CoverageAsync(service, movie.Id, cancellationToken).ConfigureAwait(false)).Covered)
        {
            return request.To(refreshed.HasFile ? RequestState.Importing : RequestState.Downloading, context.Now);
        }

        await AlignProfileAsync(service, refreshed, selection.Language!, cancellationToken).ConfigureAwait(false);
        await Arr.GrabReleaseAsync(service, release, cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("Request {RequestId}: grabbed a release for film {TmdbId} in {Language}", request.Id, request.TmdbId, selection.Language!.Code);
        return request.To(RequestState.Downloading, context.Now) with
        {
            ChosenLanguage = selection.Language.Code,
            ReleaseTitle = release.Title,
            GrabbedAt = context.Now,
            Attempts = request.Attempts + 1,
            Progress = new RequestProgress(0, null, null)
        };
    }

    /// <inheritdoc />
    protected override async Task<MediaRequest> TrackCoreAsync(MediaRequest request, ServiceSettings service, RequestContext context, CancellationToken cancellationToken)
    {
        if (_library.HasMovie(request.TmdbId))
        {
            return request.To(RequestState.Available, context.Now);
        }

        var movie = await Arr.FindMovieAsync(service, request.TmdbId, cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return request.Fail(context.Now, RequestProblemCode.TitleUnknown, "The film was removed from Radarr.");
        }

        var queue = await Arr.GetMovieQueueAsync(service, movie.Id, cancellationToken).ConfigureAwait(false);
        if (queue.Count > 0)
        {
            return await FollowQueueAsync(request, queue, context, cancellationToken).ConfigureAwait(false);
        }

        if (movie.HasFile)
        {
            // In the library under the folder Radarr imported to, though identified as another film: it is there,
            // and it gets its TMDB id back
            if (_library.ClaimMovie(request.TmdbId, movie.Path))
            {
                return request.To(RequestState.Available, context.Now);
            }

            _library.Refresh(movie.Path);
            return request.To(RequestState.Importing, context.Now);
        }

        var history = await Arr.GetMovieHistoryAsync(service, movie.Id, cancellationToken).ConfigureAwait(false);
        if (DuplicateGuard.Check(false, [], history).Covered)
        {
            // Grabbed, not in the queue yet: the queue fills a while after the grab.
            return request.State == RequestState.WaitingForSources ? request.To(RequestState.Downloading, context.Now) : request;
        }

        return NothingInFlight(request, LastFailure(request, history), context);
    }

    private async Task<Coverage> CoverageAsync(ServiceSettings service, int movieId, CancellationToken cancellationToken)
    {
        var queue = await Arr.GetMovieQueueAsync(service, movieId, cancellationToken).ConfigureAwait(false);
        var history = await Arr.GetMovieHistoryAsync(service, movieId, cancellationToken).ConfigureAwait(false);
        return DuplicateGuard.Check(false, queue, history);
    }

    private async Task<(ArrMovie? Movie, MediaRequest? Failed)> AddAsync(
        MediaRequest request,
        ServiceSettings service,
        TitleMetadata metadata,
        LanguageCandidate firstLanguage,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var folders = await Arr.GetRootFoldersAsync(service, cancellationToken).ConfigureAwait(false);
        var category = MediaLayout.Classify(metadata.GenreIds, metadata.CountryCodes, metadata.OriginalLanguage);
        if (MediaLayout.PickRootFolder(category, MediaLayout.RadarrRootFolders, folders) is not { } rootFolder)
        {
            return (null, request.Fail(context.Now, RequestProblemCode.NoRootFolder, "Radarr has no root folder to file the film in."));
        }

        if (await ProfileIdAsync(service, firstLanguage, cancellationToken).ConfigureAwait(false) is not int profileId)
        {
            return (null, request.Fail(context.Now, RequestProblemCode.ServiceUnavailable, "Radarr has no quality profile."));
        }

        JsonObject movie;
        try
        {
            movie = await Arr.LookupMovieAsync(service, request.TmdbId, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex) when (ex.Failure == ArrFailure.NotFound)
        {
            return (null, request.Fail(context.Now, RequestProblemCode.TitleUnknown, "Radarr does not find this film."));
        }

        movie["qualityProfileId"] = profileId;
        movie["rootFolderPath"] = rootFolder;
        movie["monitored"] = true;
        movie["minimumAvailability"] = "released";
        movie["addOptions"] = new JsonObject { ["searchForMovie"] = false, ["monitor"] = "movieOnly" };
        movie["tags"] ??= new JsonArray();
        try
        {
            return (await Arr.AddMovieAsync(service, movie, cancellationToken).ConfigureAwait(false), null);
        }
        catch (ArrApiException ex) when (ex.Failure == ArrFailure.Rejected)
        {
            // Added by someone else in the meantime.
            if (await Arr.FindMovieAsync(service, request.TmdbId, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                return (existing, null);
            }

            throw;
        }
    }

    private async Task AlignProfileAsync(ServiceSettings service, ArrMovie movie, LanguageCandidate language, CancellationToken cancellationToken)
    {
        // Monitored, so Radarr keeps watching its feeds for it; in the profile of the language taken, when it has a Pufferfin profile.
        var profileId = await AlignedProfileIdAsync(service, movie.QualityProfileId, language, cancellationToken).ConfigureAwait(false);
        if (profileId is null && movie.Monitored)
        {
            return;
        }

        var raw = await Arr.GetMovieObjectAsync(service, movie.Id, cancellationToken).ConfigureAwait(false);
        raw["monitored"] = true;
        if (profileId is int id)
        {
            raw["qualityProfileId"] = id;
        }

        await Arr.UpdateMovieAsync(service, movie.Id, raw, cancellationToken).ConfigureAwait(false);
    }
}
