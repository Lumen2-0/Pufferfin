using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pufferfin.Catalog.Library;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Search;
using Pufferfin.Catalog.Tests.TestSupport;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class UniversalSearchServiceTests
{
    private static readonly Guid _userId = Guid.NewGuid();
    private static readonly Guid _localMatrixId = Guid.NewGuid();

    private static HttpResponseMessage TmdbOk(HttpRequestMessage request)
        => request.RequestUri!.AbsolutePath == "/3/search/multi"
            ? FakeHttpHandler.Json(Fixtures.TmdbSearchMatrix)
            : FakeHttpHandler.Status(HttpStatusCode.NotFound);

    private static CatalogTestHost CreateHost(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? tmdb = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? audible = null,
        string? cacheDirectory = null)
    {
        var host = new CatalogTestHost(
            tmdb ?? ((request, _) => Task.FromResult(TmdbOk(request))),
            audible ?? ((_, _) => Task.FromResult(FakeHttpHandler.Json(Fixtures.AudibleSearchDune))),
            cacheDirectory: cacheDirectory);
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = _localMatrixId, Kind = CatalogItemKind.Movie, Title = "The Matrix", Year = 1999, TmdbId = 603 });
        return host;
    }

    [Fact]
    public async Task Search_ReturnsMoviesPeopleAndAudiobooksInOneResponse_WithAvailability()
    {
        using var host = CreateHost();
        host.Library.Items.Add(new LocalCatalogEntry { ItemId = Guid.NewGuid(), Kind = CatalogItemKind.Audiobook, Title = "Matrix audio", Asin = "B0FR4NC3D1" });
        var search = host.Get<UniversalSearchService>();

        var response = await search.SearchAsync(new CatalogSearchRequest { Query = "  matrix " }, _userId, CancellationToken.None);

        Assert.Equal("matrix", response.Query);
        Assert.Contains(response.Items, i => i.Kind == CatalogItemKind.Movie);
        Assert.Contains(response.Items, i => i.Kind == CatalogItemKind.Person);
        Assert.Contains(response.Items, i => i.Kind == CatalogItemKind.Audiobook);
        Assert.All(response.Items, i => Assert.True(Enum.IsDefined(i.Availability)));
        Assert.Equal(
            [CatalogSource.Library, CatalogSource.Tmdb, CatalogSource.Audible],
            response.Sources.Select(s => s.Source));
        Assert.All(response.Sources, s => Assert.Equal(CatalogSourceStatus.Ok, s.Status));
    }

    [Fact]
    public async Task Search_TitleOnDiskAndOnTmdb_IsASingleEntryMarkedOnDisk()
    {
        using var host = CreateHost();
        var search = host.Get<UniversalSearchService>();

        var response = await search.SearchAsync(new CatalogSearchRequest { Query = "matrix", Kinds = [CatalogItemKind.Movie] }, _userId, CancellationToken.None);

        var matrix = Assert.Single(response.Items, i => i.TmdbId == 603);
        Assert.Equal(CatalogAvailability.OnDisk, matrix.Availability);
        Assert.Equal(_localMatrixId, matrix.LocalItemId);
        Assert.Equal("Matrix", matrix.Title);
        Assert.Equal(CatalogAvailability.Missing, Assert.Single(response.Items, i => i.TmdbId == 604).Availability);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task Search_SameQueryTwice_CallsEachExternalCatalogOnce()
    {
        using var host = CreateHost();
        var search = host.Get<UniversalSearchService>();

        await search.SearchAsync(new CatalogSearchRequest { Query = "Matrix" }, _userId, CancellationToken.None);
        var second = await search.SearchAsync(new CatalogSearchRequest { Query = "matrix " }, _userId, CancellationToken.None);

        Assert.Single(host.Tmdb.Requests);
        Assert.Single(host.Audible.Requests);
        Assert.Contains(second.Items, i => i.TmdbId == 603);
    }

    [Fact]
    public async Task Search_AfterRestart_IsServedFromTheCacheDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pufferfin-catalog-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = CreateHost(cacheDirectory: directory))
            {
                await first.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);
                Assert.Single(first.Tmdb.Requests);
            }

            using var restarted = CreateHost(cacheDirectory: directory);
            var response = await restarted.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);

            Assert.Empty(restarted.Tmdb.Requests);
            Assert.Empty(restarted.Audible.Requests);
            Assert.Contains(response.Items, i => i.TmdbId == 604);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Search_TmdbUnreachable_StillReturnsOtherSources_AndReportsTheFailure()
    {
        using var host = CreateHost(tmdb: (_, _) => throw new HttpRequestException("Connection refused (api.themoviedb.org:443)"));
        var search = host.Get<UniversalSearchService>();

        var response = await search.SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);

        Assert.Equal(CatalogSourceStatus.Unavailable, response.Sources.Single(s => s.Source == CatalogSource.Tmdb).Status);
        Assert.Equal(CatalogSourceStatus.Ok, response.Sources.Single(s => s.Source == CatalogSource.Library).Status);
        Assert.Equal(CatalogSourceStatus.Ok, response.Sources.Single(s => s.Source == CatalogSource.Audible).Status);
        Assert.Contains(response.Items, i => _localMatrixId.Equals(i.LocalItemId ?? Guid.Empty) && i.Availability == CatalogAvailability.OnDisk);
        Assert.Contains(response.Items, i => i.Kind == CatalogItemKind.Audiobook);

        // A failed call is not cached: the next search tries TMDB again (two attempts per search, bounded retry).
        await search.SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);
        Assert.Equal(4, host.Tmdb.Requests.Count);
    }

    [Fact]
    public async Task Search_TmdbTooSlow_ReportsTimeoutWithoutBlockingTheOthers()
    {
        using var host = CreateHost(tmdb: async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpHandler.Status(HttpStatusCode.OK);
        });
        var search = host.Get<UniversalSearchService>();
        search.SuggestionDeadline = TimeSpan.FromMilliseconds(200);

        var response = await search.SearchAsync(new CatalogSearchRequest { Query = "matrix", IsSuggestion = true }, _userId, CancellationToken.None);

        Assert.Equal(CatalogSourceStatus.TimedOut, response.Sources.Single(s => s.Source == CatalogSource.Tmdb).Status);
        Assert.Contains(response.Items, i => _localMatrixId.Equals(i.LocalItemId ?? Guid.Empty));
    }

    [Fact]
    public async Task Search_TmdbRefusesTheKey_ReportsUnauthorized_WithoutRetrying()
    {
        using var host = CreateHost(tmdb: (_, _) => Task.FromResult(FakeHttpHandler.Json("""{"status_code":7,"status_message":"Invalid API key"}""", HttpStatusCode.Unauthorized)));

        var response = await host.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);

        Assert.Equal(CatalogSourceStatus.Unauthorized, response.Sources.Single(s => s.Source == CatalogSource.Tmdb).Status);
        Assert.Single(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Search_UnconfiguredCatalogs_AreReportedDisabled_AndNeverCalled()
    {
        using var host = CreateHost();
        host.Settings = new() { TmdbApiKey = " ", AudibleMarkets = [] };

        var response = await host.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);

        Assert.Equal(CatalogSourceStatus.Disabled, response.Sources.Single(s => s.Source == CatalogSource.Tmdb).Status);
        Assert.Equal(CatalogSourceStatus.Disabled, response.Sources.Single(s => s.Source == CatalogSource.Audible).Status);
        Assert.Empty(host.Tmdb.Requests);
        Assert.Empty(host.Audible.Requests);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task Search_LibraryFailure_StillReturnsExternalResults()
    {
        using var host = CreateHost();
        host.Library.SearchFailure = new InvalidOperationException("database locked");

        var response = await host.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);

        Assert.Equal(CatalogSourceStatus.Unavailable, response.Sources.Single(s => s.Source == CatalogSource.Library).Status);
        Assert.Contains(response.Items, i => i.TmdbId == 604);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" a ")]
    public async Task Search_TooShortQuery_QueriesNothing(string query)
    {
        using var host = CreateHost();

        var response = await host.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = query }, _userId, CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Empty(host.Tmdb.Requests);
    }

    [Fact]
    public async Task Search_KindsFilter_SkipsUnrequestedCatalogs()
    {
        using var host = CreateHost();

        var response = await host.Get<UniversalSearchService>().SearchAsync(new CatalogSearchRequest { Query = "matrix", Kinds = [CatalogItemKind.Person] }, _userId, CancellationToken.None);

        Assert.All(response.Items, i => Assert.Equal(CatalogItemKind.Person, i.Kind));
        Assert.Empty(host.Audible.Requests);
        Assert.DoesNotContain(response.Sources, s => s.Source == CatalogSource.Audible);
    }

    [Fact]
    public async Task TmdbKey_NeverAppearsInResponsesLogsOrCache()
    {
        var calls = 0;
        using var host = CreateHost(tmdb: (request, _) =>
        {
            // First a network failure carrying the full URL in its message, then a normal answer.
            if (Interlocked.Increment(ref calls) <= 2)
            {
                throw new HttpRequestException("Failed GET " + request.RequestUri);
            }

            return Task.FromResult(TmdbOk(request));
        });
        var search = host.Get<UniversalSearchService>();

        var failed = await search.SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);
        var succeeded = await search.SearchAsync(new CatalogSearchRequest { Query = "matrix" }, _userId, CancellationToken.None);

        // The key was really sent, so the checks below are meaningful.
        Assert.Contains(host.Tmdb.Requests, uri => uri.Query.Contains(CatalogTestHost.TmdbKey, StringComparison.Ordinal));

        Assert.DoesNotContain(CatalogTestHost.TmdbKey, JsonSerializer.Serialize(failed), StringComparison.Ordinal);
        Assert.DoesNotContain(CatalogTestHost.TmdbKey, JsonSerializer.Serialize(succeeded), StringComparison.Ordinal);
        Assert.NotEmpty(host.Logs.Entries);
        Assert.All(host.Logs.Entries, entry => Assert.DoesNotContain(CatalogTestHost.TmdbKey, entry, StringComparison.Ordinal));
        Assert.All(host.Logs.Entries, entry => Assert.DoesNotContain("api_key", entry, StringComparison.Ordinal));
        foreach (var file in Directory.EnumerateFiles(host.CacheDirectory))
        {
            Assert.DoesNotContain(CatalogTestHost.TmdbKey, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
    }
}
