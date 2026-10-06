using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Catalog.Http;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests.Integration;

namespace Pufferfin.Server.Requests.Workflow;

/// <summary>
/// Audiobooks, through Shelfmark.
/// </summary>
/// <remarks>
/// <para>
/// The search: read the book from Audible, check the library, check the Shelfmark downloads (queued, in progress and
/// complete) against a double download, search the releases in the language of the request, pick one (never a complete
/// edition), check the downloads again (the search is slow), then queue it.
/// </para>
/// <para>
/// The tracking: follow the Shelfmark download; once complete, file the book in <c>Author/Title</c> as hard links with
/// its cover, then find the qBittorrent torrent it came from by inode.
/// </para>
/// </remarks>
public sealed class AudiobookWorkflow : IRequestWorkflow
{
    /// <summary>
    /// The number of searches after which a request whose downloads keep failing stops.
    /// </summary>
    public const int MaxSearchAttempts = ArrWorkflowBase.MaxSearchAttempts;

    private readonly ShelfmarkClient _shelfmark;
    private readonly IAudiobookMetadata _metadata;
    private readonly AudiobookLibrary _library;
    private readonly AudiobookTorrentLocator _locator;
    private readonly IMediaLibrary _media;
    private readonly ILogger<AudiobookWorkflow> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiobookWorkflow"/> class.
    /// </summary>
    /// <param name="shelfmark">The Shelfmark client.</param>
    /// <param name="metadata">The Audible metadata.</param>
    /// <param name="library">The audiobook library.</param>
    /// <param name="locator">Finds the torrent of a book by inode.</param>
    /// <param name="media">The Jellyfin library.</param>
    /// <param name="logger">The logger.</param>
    public AudiobookWorkflow(
        ShelfmarkClient shelfmark,
        IAudiobookMetadata metadata,
        AudiobookLibrary library,
        AudiobookTorrentLocator locator,
        IMediaLibrary media,
        ILogger<AudiobookWorkflow> logger)
    {
        _shelfmark = shelfmark;
        _metadata = metadata;
        _library = library;
        _locator = locator;
        _media = media;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool Handles(RequestKind kind) => kind == RequestKind.Audiobook;

    /// <inheritdoc />
    public async Task<MediaRequest> SearchAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Settings.GetService(PufferfinService.Shelfmark) is not { } service)
        {
            return request.Fail(context.Now, RequestProblemCode.ServiceDisabled, "Shelfmark is not configured.");
        }

        try
        {
            var book = request.Asin is null ? null : await _metadata.GetAsync(request.Asin, request.Market, cancellationToken).ConfigureAwait(false);
            if (book is null)
            {
                return request.Fail(context.Now, RequestProblemCode.TitleUnknown, "Audible does not know this audiobook.");
            }

            if (_library.Contains(book))
            {
                return request.To(RequestState.Available, context.Now) with { LibraryPath = _library.FolderOf(book) };
            }

            // Queued, in progress or complete in Shelfmark, by Pufferfin or by hand: never twice.
            if (Existing(request, book, await _shelfmark.GetDownloadsAsync(service, cancellationToken).ConfigureAwait(false)) is { } existing)
            {
                return Adopt(request, existing, context);
            }

            var language = WantedLanguage(request, book);
            var releases = await _shelfmark.SearchAudiobookReleasesAsync(service, book, language, cancellationToken).ConfigureAwait(false);
            var selection = AudiobookReleaseSelector.Select(releases, book, language ?? string.Empty, request.ExcludedReleases);
            if (selection.Release is not { } release)
            {
                var searched = request with { Attempts = request.Attempts + 1 };
                return selection.Problem!.Code == RequestProblemCode.NoRelease
                    ? searched.Wait(context.Now, selection.Problem with { Message = "No release was found yet. Pufferfin will search again." })
                    : searched.To(RequestState.Failed, context.Now, selection.Problem);
            }

            // The search took a while: the release may have been queued meanwhile.
            var downloads = await _shelfmark.GetDownloadsAsync(service, cancellationToken).ConfigureAwait(false);
            if (downloads.TryGetValue(release.SourceId, out var queued) && !queued.IsFailed && !queued.IsCancelled)
            {
                return Adopt(request, queued, context);
            }

            if (Existing(request, book, downloads) is { } appeared)
            {
                return Adopt(request, appeared, context);
            }

            await _shelfmark.QueueAsync(service, release, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Request {RequestId}: queued an audiobook release in Shelfmark for {Asin}", request.Id, request.Asin);
            return request.To(RequestState.Downloading, context.Now) with
            {
                DownloadId = release.SourceId,
                ChosenLanguage = language,
                ReleaseTitle = release.Title,
                GrabbedAt = context.Now,
                Attempts = request.Attempts + 1,
                Progress = new RequestProgress(0, null, null)
            };
        }
        catch (ArrApiException ex) when (ex.Service == PufferfinService.Shelfmark && ex.StatusCode == 503)
        {
            // Shelfmark answers 503 when no release source could be searched.
            return (request with { Attempts = request.Attempts + 1 }).Wait(context.Now, new RequestProblem(
                RequestProblemCode.IndexersUnavailable,
                "The release sources of Shelfmark cannot be reached for now. Pufferfin will search again.",
                Detail(ex, context)));
        }
        catch (ArrApiException ex)
        {
            return OnServiceFailure(request, ex, context);
        }
        catch (CatalogSourceException)
        {
            return Unavailable(request, "Audible cannot be reached for now. Pufferfin will try again.", null, context);
        }
    }

    /// <inheritdoc />
    public async Task<MediaRequest> TrackAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // A service turned off meanwhile, or nothing queued yet: nothing to follow, the request keeps its state.
        if (context.Settings.GetService(PufferfinService.Shelfmark) is not { } service || request.DownloadId is null)
        {
            return request;
        }

        try
        {
            var downloads = await _shelfmark.GetDownloadsAsync(service, cancellationToken).ConfigureAwait(false);
            if (!downloads.TryGetValue(request.DownloadId, out var download))
            {
                return await GoneAsync(request, context, cancellationToken).ConfigureAwait(false);
            }

            if (download.IsActive)
            {
                var downloading = request.To(RequestState.Downloading, context.Now);
                var progress = new RequestProgress(Math.Round(Math.Clamp(download.Progress / 100, 0, 1), 4), null, null);
                return progress == downloading.Progress ? downloading : downloading with { Progress = progress };
            }

            if (download.IsFailed || download.IsCancelled)
            {
                var reason = download.IsCancelled
                    ? "The download was cancelled in Shelfmark."
                    : SecretRedactor.Redact(download.StatusMessage ?? "The download failed.", context.Settings);
                return Failed(request, reason, context);
            }

            return download.IsComplete ? await ImportAsync(request, service, download, context, cancellationToken).ConfigureAwait(false) : request;
        }
        catch (ArrApiException ex)
        {
            return OnServiceFailure(request, ex, context);
        }
        catch (CatalogSourceException)
        {
            return Unavailable(request, "Audible cannot be reached for now. Pufferfin will try again.", null, context);
        }
    }

    private static string? WantedLanguage(MediaRequest request, AudiobookMetadata book)
        => request.Language == MediaRequest.OriginalLanguage ? book.Language : request.Language;

    private static ShelfmarkDownload? Existing(MediaRequest request, AudiobookMetadata book, IReadOnlyDictionary<string, ShelfmarkDownload> downloads)
        => downloads.Values.FirstOrDefault(d => (d.IsActive || d.IsComplete)
            && !request.ExcludedReleases.Contains(d.Id ?? string.Empty)
            && (d.Id == request.DownloadId || (d.Title is { } title && AudiobookReleaseSelector.IsBook(title, book))));

    private static MediaRequest Adopt(MediaRequest request, ShelfmarkDownload download, RequestContext context)
    {
        // Tracking takes it from here: progress, then the import once complete.
        var adopted = request.To(RequestState.Downloading, context.Now) with { DownloadId = download.Id, GrabbedAt = request.GrabbedAt ?? context.Now };
        return download.IsComplete ? adopted : adopted with { Progress = new RequestProgress(Math.Clamp(download.Progress / 100, 0, 1), null, null) };
    }

    private static MediaRequest Failed(MediaRequest request, string reason, RequestContext context)
    {
        // The release is never taken again for this request: the next search picks another one.
        var excluded = request with { ExcludedReleases = [.. request.ExcludedReleases, request.DownloadId!], DownloadId = null };
        return request.Attempts >= MaxSearchAttempts
            ? excluded.Fail(context.Now, RequestProblemCode.DownloadFailed, $"The download failed {request.Attempts} times.", reason)
            : excluded.To(RequestState.Searching, context.Now, new RequestProblem(RequestProblemCode.DownloadFailed, "The download failed. Pufferfin searches for another release.", reason));
    }

    private static string? Detail(ArrApiException ex, RequestContext context)
        => ex.RemoteMessages.Count == 0 ? null : SecretRedactor.Redact(string.Join(" ", ex.RemoteMessages), context.Settings);

    private static MediaRequest Unavailable(MediaRequest request, string message, string? detail, RequestContext context)
    {
        // Unreachable, slow or refusing the key: transient, or for an administrator to fix. The request keeps its state.
        var problem = new RequestProblem(RequestProblemCode.ServiceUnavailable, message, detail);
        var kept = request.Problem == problem ? request : request with { Problem = problem, UpdatedAt = context.Now };
        return request.State == RequestState.Searching ? kept with { NextAttemptAt = context.Now + ArrWorkflowBase.UnreachableRetryDelay } : kept;
    }

    private MediaRequest OnServiceFailure(MediaRequest request, ArrApiException ex, RequestContext context)
    {
        _logger.LogWarning("Request {RequestId}: {Failure}", request.Id, ex.Message);
        if (ex.Failure is ArrFailure.Rejected or ArrFailure.NotFound && request.State == RequestState.Searching)
        {
            return request.Fail(context.Now, RequestProblemCode.ServiceUnavailable, ex.Message, Detail(ex, context));
        }

        return Unavailable(request, ex.Message, Detail(ex, context), context);
    }

    private async Task<MediaRequest> GoneAsync(MediaRequest request, RequestContext context, CancellationToken cancellationToken)
    {
        // Shelfmark forgets its downloads when it restarts. Filed already: done.
        if (request.Asin is not null && await _metadata.GetAsync(request.Asin, request.Market, cancellationToken).ConfigureAwait(false) is { } book && _library.Contains(book))
        {
            return request.To(RequestState.Available, context.Now) with { LibraryPath = _library.FolderOf(book) };
        }

        if (request.GrabbedAt is { } grabbedAt && context.Now - grabbedAt < ArrWorkflowBase.GrabGracePeriod)
        {
            return request;
        }

        return request.Attempts >= MaxSearchAttempts
            ? (request with { DownloadId = null }).Wait(context.Now, new RequestProblem(RequestProblemCode.NoRelease, "No download got through yet. Pufferfin will search again."))
            : (request with { DownloadId = null }).To(RequestState.Searching, context.Now, new RequestProblem(RequestProblemCode.DownloadFailed, "Shelfmark no longer knows the download. Pufferfin searches again."));
    }

    private async Task<MediaRequest> ImportAsync(MediaRequest request, ServiceSettings service, ShelfmarkDownload download, RequestContext context, CancellationToken cancellationToken)
    {
        var book = request.Asin is null ? null : await _metadata.GetAsync(request.Asin, request.Market, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return request.Fail(context.Now, RequestProblemCode.TitleUnknown, "Audible no longer knows this audiobook.");
        }

        if (string.IsNullOrWhiteSpace(download.DownloadPath))
        {
            return request.Fail(context.Now, RequestProblemCode.ImportBlocked, "Shelfmark reports the download complete, but not where its files are.");
        }

        AudiobookImport import;
        try
        {
            import = await _library.ImportAsync(download.DownloadPath, book, cancellationToken).ConfigureAwait(false);
        }
        catch (AudiobookImportException ex)
        {
            // Followed again every round: once the cause is fixed, the import goes through.
            return request.Fail(context.Now, RequestProblemCode.ImportBlocked, "The audiobook cannot be filed in the library.", ex.Message);
        }

        _media.Refresh(import.Folder);
        _logger.LogInformation("Request {RequestId}: filed {Files} audiobook files in {Folder}", request.Id, import.Files, import.Folder);

        string? hash = null;
        if (context.Settings.GetService(PufferfinService.QBittorrent) is { } qBittorrent)
        {
            try
            {
                hash = await _locator.FindAsync(qBittorrent, import.Folder, cancellationToken).ConfigureAwait(false);
            }
            catch (ArrApiException ex)
            {
                // The book is filed; the link to its torrent is found again on the next import or by the seed policy.
                _logger.LogDebug("Request {RequestId}: torrent lookup unavailable ({Failure})", request.Id, ex.Failure);
            }
        }

        RequestProblem? problem = null;
        if (import.NotWritable.Count > 0)
        {
            _logger.LogWarning("Request {RequestId}: Pufferfin cannot change or delete {Count} paths in {Folder}", request.Id, import.NotWritable.Count, import.Folder);
            problem = new RequestProblem(
                RequestProblemCode.FilesNotWritable,
                "Pufferfin cannot change or delete some files of this audiobook, so deleting it from Pufferfin would fail. Check their owner and permissions.",
                string.Join(" ", import.NotWritable.Take(3)));
        }

        return request.To(RequestState.Available, context.Now, problem) with { LibraryPath = import.Folder, TorrentHash = hash ?? request.TorrentHash };
    }
}
