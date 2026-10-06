using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pufferfin.Catalog.Audible;
using Pufferfin.Catalog.Http;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Tmdb;

namespace Pufferfin.Catalog.Search;

/// <summary>
/// Queries the local library, TMDB and Audible in parallel and returns one merged list.
/// </summary>
/// <remarks>
/// Each external source has its own deadline. A source that fails, times out or is not configured is reported in
/// <see cref="CatalogSearchResponse.Sources"/> and the others are still returned.
/// </remarks>
public sealed class UniversalSearchService
{
    /// <summary>
    /// The shortest query that is sent to the sources.
    /// </summary>
    public const int MinQueryLength = 2;

    /// <summary>
    /// The longest accepted query.
    /// </summary>
    public const int MaxQueryLength = 200;

    /// <summary>
    /// The largest accepted limit.
    /// </summary>
    public const int MaxLimit = 50;

    private static readonly CatalogItemKind[] _allKinds = Enum.GetValues<CatalogItemKind>();

    private readonly ILocalCatalogIndex _index;
    private readonly TmdbCatalogService _tmdb;
    private readonly AudibleCatalogService _audible;
    private readonly ILogger<UniversalSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UniversalSearchService"/> class.
    /// </summary>
    /// <param name="index">The local library index.</param>
    /// <param name="tmdb">The TMDB catalog.</param>
    /// <param name="audible">The Audible catalog.</param>
    /// <param name="logger">The logger.</param>
    public UniversalSearchService(ILocalCatalogIndex index, TmdbCatalogService tmdb, AudibleCatalogService audible, ILogger<UniversalSearchService> logger)
    {
        _index = index;
        _tmdb = tmdb;
        _audible = audible;
        _logger = logger;
    }

    /// <summary>
    /// Gets or sets the deadline of each external source for a full search.
    /// </summary>
    public TimeSpan SearchDeadline { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Gets or sets the deadline of each external source for a type-ahead suggestion.
    /// </summary>
    public TimeSpan SuggestionDeadline { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Normalizes a query: trimmed, inner whitespace collapsed, capped at <see cref="MaxQueryLength"/>.
    /// </summary>
    /// <param name="query">The raw query.</param>
    /// <returns>The normalized query.</returns>
    public static string NormalizeQuery(string? query)
    {
        var parts = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var normalized = string.Join(' ', parts);
        return normalized.Length > MaxQueryLength ? normalized[..MaxQueryLength] : normalized;
    }

    /// <summary>
    /// Runs a universal search.
    /// </summary>
    /// <param name="request">The search.</param>
    /// <param name="userId">The user the library is filtered for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The merged entries and the status of each source.</returns>
    public async Task<CatalogSearchResponse> SearchAsync(CatalogSearchRequest request, Guid userId, CancellationToken cancellationToken)
    {
        var query = NormalizeQuery(request.Query);
        if (query.Length < MinQueryLength)
        {
            return new CatalogSearchResponse { Query = query, Items = [], Sources = [] };
        }

        var kinds = request.Kinds.Count == 0 ? _allKinds : request.Kinds.Distinct().ToArray();
        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var deadline = request.IsSuggestion ? SuggestionDeadline : SearchDeadline;
        var tmdbKinds = kinds.Where(k => k != CatalogItemKind.Audiobook).ToHashSet();

        var localTask = RunAsync(
            CatalogSource.Library,
            null,
            ct => _index.SearchAsync(query, userId, kinds, limit, ct),
            cancellationToken);
        var tmdbTask = tmdbKinds.Count == 0
            ? Task.FromResult<(IReadOnlyList<CatalogItem>, CatalogSourceReport?)>(([], null))
            : RunAsync(
                CatalogSource.Tmdb,
                deadline,
                async ct => (IReadOnlyList<CatalogItem>)(await _tmdb.SearchAsync(query, ct).ConfigureAwait(false)).Where(i => tmdbKinds.Contains(i.Kind)).ToList(),
                cancellationToken);
        var audibleTask = !kinds.Contains(CatalogItemKind.Audiobook)
            ? Task.FromResult<(IReadOnlyList<CatalogItem>, CatalogSourceReport?)>(([], null))
            : RunAsync(
                CatalogSource.Audible,
                deadline,
                ct => _audible.SearchAsync(new AudibleQuery { Keywords = query, Language = request.AudiobookLanguage, Limit = limit }, ct),
                cancellationToken);

        var (local, localReport) = await localTask.ConfigureAwait(false);
        var (tmdb, tmdbReport) = await tmdbTask.ConfigureAwait(false);
        var (audible, audibleReport) = await audibleTask.ConfigureAwait(false);

        return new CatalogSearchResponse
        {
            Query = query,
            Items = CatalogMerger.Merge(local, tmdb, audible, limit),
            Sources = new[] { localReport, tmdbReport, audibleReport }.OfType<CatalogSourceReport>().ToList()
        };
    }

    private async Task<(IReadOnlyList<T> Items, CatalogSourceReport? Report)> RunAsync<T>(
        CatalogSource source,
        TimeSpan? deadline,
        Func<CancellationToken, Task<IReadOnlyList<T>>> query,
        CancellationToken cancellationToken)
    {
        using var deadlineSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (deadline is TimeSpan timeout)
        {
            deadlineSource.CancelAfter(timeout);
        }

        CatalogSourceStatus status;
        try
        {
            var items = await query(deadlineSource.Token).ConfigureAwait(false);
            return (items, new CatalogSourceReport(source, CatalogSourceStatus.Ok));
        }
        catch (CatalogSourceException ex)
        {
            status = ex.Status;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status = CatalogSourceStatus.TimedOut;
        }
        catch (Exception ex) when (source == CatalogSource.Library && ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Local library search failed");
            status = CatalogSourceStatus.Unavailable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only the type is logged: an unexpected exception from an HTTP stack may carry the request URL.
            _logger.LogWarning("Catalog search on {Source} failed with {ExceptionType}", source, ex.GetType().Name);
            status = CatalogSourceStatus.Unavailable;
        }

        if (status != CatalogSourceStatus.Disabled)
        {
            _logger.LogInformation("Catalog search on {Source} degraded: {Status}", source, status);
        }

        return ([], new CatalogSourceReport(source, status));
    }
}
