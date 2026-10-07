using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Server.Arr;
using Pufferfin.Server.Audiobooks;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Ebooks;
using Pufferfin.Server.Requests.Integration;

namespace Pufferfin.Server.Requests.Workflow;

/// <summary>
/// Books to read, through Shelfmark: the same client, queue and tracking as the audiobooks, with the <c>ebook</c> content type.
/// </summary>
/// <remarks>
/// <para>
/// The search: read the book from its metadata provider, check the library, check the Shelfmark downloads (queued, in
/// progress and complete) against a double download, search the ebook releases in the language of the request, pick one
/// (never a complete edition, never a format the client cannot open), check the downloads again, then queue it.
/// </para>
/// <para>
/// The tracking: follow the Shelfmark download; once complete, file the book in <c>Author/Title</c> as a hard link with
/// its cover, then find the qBittorrent torrent it came from by inode.
/// </para>
/// </remarks>
public sealed class EbookWorkflow : IRequestWorkflow
{
    /// <summary>
    /// The number of searches after which a request whose downloads keep failing stops.
    /// </summary>
    public const int MaxSearchAttempts = ArrWorkflowBase.MaxSearchAttempts;

    private readonly ShelfmarkClient _shelfmark;
    private readonly IEbookMetadata _metadata;
    private readonly EbookLibrary _library;
    private readonly AudiobookTorrentLocator _locator;
    private readonly IMediaLibrary _media;
    private readonly ILogger<EbookWorkflow> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EbookWorkflow"/> class.
    /// </summary>
    /// <param name="shelfmark">The Shelfmark client.</param>
    /// <param name="metadata">The metadata providers of Shelfmark.</param>
    /// <param name="library">The book library.</param>
    /// <param name="locator">Finds the torrent of a book by inode.</param>
    /// <param name="media">The Jellyfin library.</param>
    /// <param name="logger">The logger.</param>
    public EbookWorkflow(
        ShelfmarkClient shelfmark,
        IEbookMetadata metadata,
        EbookLibrary library,
        AudiobookTorrentLocator locator,
        IMediaLibrary media,
        ILogger<EbookWorkflow> logger)
    {
        _shelfmark = shelfmark;
        _metadata = metadata;
        _library = library;
        _locator = locator;
        _media = media;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool Handles(RequestKind kind) => kind == RequestKind.Ebook;

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
            var book = await GetBookAsync(request, service, cancellationToken).ConfigureAwait(false);
            if (book is null)
            {
                return request.Fail(context.Now, RequestProblemCode.TitleUnknown, "The metadata provider of Shelfmark does not know this book.");
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
            var releases = await _shelfmark.SearchEbookReleasesAsync(service, book, language, cancellationToken).ConfigureAwait(false);
            var selection = EbookReleaseSelector.Select(releases, book, language ?? string.Empty, request.ExcludedReleases);
            if (selection.Release is not { } release)
            {
                var searched = request with { Attempts = request.Attempts + 1 };
                return selection.Problem!.Code == RequestProblemCode.NoRelease
                    ? searched.Wait(context.Now, selection.Problem with { Message = selection.Problem.Message + " Pufferfin will search again." })
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

            await _shelfmark.QueueAsync(service, release, ShelfmarkClient.EbookContentType, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Request {RequestId}: queued an ebook release in Shelfmark for {Provider}:{BookId}", request.Id, request.BookProvider, request.BookId);
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
            // Shelfmark answers 503 when no release source, or no metadata provider, could be reached.
            return (request with { Attempts = request.Attempts + 1 }).Wait(context.Now, new RequestProblem(
                RequestProblemCode.IndexersUnavailable,
                "The release sources or the metadata provider of Shelfmark cannot be reached for now. Pufferfin will search again.",
                Detail(ex, context)));
        }
        catch (ArrApiException ex)
        {
            return OnServiceFailure(request, ex, context);
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
                return await GoneAsync(request, service, context, cancellationToken).ConfigureAwait(false);
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
    }

    /// <summary>
    /// Gets a value indicating whether a Shelfmark download may be the book of a request: not an audiobook.
    /// </summary>
    /// <param name="download">The download.</param>
    /// <returns><c>true</c> for an ebook download, or one that does not say.</returns>
    internal static bool IsEbookDownload(ShelfmarkDownload download)
        => !string.Equals(download.ContentType, ShelfmarkClient.AudiobookContentType, StringComparison.OrdinalIgnoreCase);

    private static string? WantedLanguage(MediaRequest request, EbookMetadata book)
        => request.Language == MediaRequest.OriginalLanguage ? book.Language : request.Language;

    // A download made elsewhere is adopted as strictly as a release is picked: an ebook, exactly this book, in the language of the request.
    private static ShelfmarkDownload? Existing(MediaRequest request, EbookMetadata book, IReadOnlyDictionary<string, ShelfmarkDownload> downloads)
        => downloads.Values.FirstOrDefault(d => (d.IsActive || d.IsComplete)
            && IsEbookDownload(d)
            && !request.ExcludedReleases.Contains(d.Id ?? string.Empty)
            && (d.Id == request.DownloadId
                || (d.Title is { } title && EbookReleaseSelector.IsBook(title, book, WantedLanguage(request, book) ?? string.Empty))));

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

    private Task<EbookMetadata?> GetBookAsync(MediaRequest request, ServiceSettings service, CancellationToken cancellationToken)
        => request.BookProvider is null || request.BookId is null
            ? Task.FromResult<EbookMetadata?>(null)
            : _metadata.GetAsync(service, request.BookProvider, request.BookId, cancellationToken);

    private MediaRequest OnServiceFailure(MediaRequest request, ArrApiException ex, RequestContext context)
    {
        _logger.LogWarning("Request {RequestId}: {Failure}", request.Id, ex.Message);
        if (ex.Failure is ArrFailure.Rejected or ArrFailure.NotFound && request.State == RequestState.Searching)
        {
            return request.Fail(context.Now, RequestProblemCode.ServiceUnavailable, ex.Message, Detail(ex, context));
        }

        return Unavailable(request, ex.Message, Detail(ex, context), context);
    }

    private async Task<MediaRequest> GoneAsync(MediaRequest request, ServiceSettings service, RequestContext context, CancellationToken cancellationToken)
    {
        // Shelfmark forgets its downloads when it restarts. Filed already: done.
        if (await GetBookAsync(request, service, cancellationToken).ConfigureAwait(false) is { } book && _library.Contains(book))
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
        var book = await GetBookAsync(request, service, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return request.Fail(context.Now, RequestProblemCode.TitleUnknown, "The metadata provider of Shelfmark no longer knows this book.");
        }

        if (string.IsNullOrWhiteSpace(download.DownloadPath))
        {
            return request.Fail(context.Now, RequestProblemCode.ImportBlocked, "Shelfmark reports the download complete, but not where its files are.");
        }

        EbookImport import;
        try
        {
            import = await _library.ImportAsync(download.DownloadPath, book, cancellationToken).ConfigureAwait(false);
        }
        catch (EbookImportException ex)
        {
            // Followed again every round: once the cause is fixed, the import goes through.
            return request.Fail(context.Now, RequestProblemCode.ImportBlocked, "The book cannot be filed in the library.", ex.Message);
        }

        _media.Refresh(import.Folder);
        _logger.LogInformation("Request {RequestId}: filed {File} in {Folder}", request.Id, System.IO.Path.GetFileName(import.File), import.Folder);

        string? hash = null;
        try
        {
            hash = await _locator.FindAsync(context.Settings, [import.TorrentFile], MediaLayout.BooksCategory, cancellationToken).ConfigureAwait(false);
        }
        catch (ArrApiException ex)
        {
            // The book is filed; the torrent can be found by inode again when it is needed.
            _logger.LogDebug("Request {RequestId}: torrent lookup unavailable ({Failure})", request.Id, ex.Failure);
        }

        RequestProblem? problem = null;
        if (import.NotWritable.Count > 0)
        {
            _logger.LogWarning("Request {RequestId}: Pufferfin cannot change or delete {Count} paths in {Folder}", request.Id, import.NotWritable.Count, import.Folder);
            problem = new RequestProblem(
                RequestProblemCode.FilesNotWritable,
                "Pufferfin cannot change or delete some files of this book, so deleting it from Pufferfin would fail. Check their owner and permissions.",
                string.Join(" ", import.NotWritable.Take(3)));
        }

        return request.To(RequestState.Available, context.Now, problem) with { LibraryPath = import.Folder, TorrentHash = hash ?? request.TorrentHash };
    }
}
