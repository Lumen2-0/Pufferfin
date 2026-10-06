using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Pufferfin.Catalog.Api;
using Pufferfin.Catalog.Models;
using Pufferfin.Catalog.Search;
using Pufferfin.Catalog.Tests.TestSupport;
using Xunit;

namespace Pufferfin.Catalog.Tests;

public sealed class CatalogControllerTests
{
    private static CatalogController CreateController(CatalogTestHost host, Guid userId)
    {
        var controller = ActivatorUtilities.CreateInstance<CatalogController>(host.Services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, userId.ToString("N"))], "Test"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task Search_BeyondTheUserBudget_Returns429WithRetryAfter_AndDoesNotQueryTheCatalogs()
    {
        using var host = new CatalogTestHost(tmdb: (_, _) => Task.FromResult(FakeHttpHandler.Json(Fixtures.TmdbSearchMatrix)));
        var controller = CreateController(host, Guid.NewGuid());

        for (var i = 0; i < SearchRateLimiter.BurstSize; i++)
        {
            var allowed = await controller.Search("query " + i, [CatalogItemKind.Movie], null, null, CancellationToken.None);
            Assert.NotNull(allowed.Value);
        }

        var tmdbCalls = host.Tmdb.Requests.Count;
        var refused = await controller.Search("one more", [CatalogItemKind.Movie], null, null, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(refused.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, result.StatusCode);
        Assert.True(int.Parse(controller.Response.Headers.RetryAfter.ToString(), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.Equal(tmdbCalls, host.Tmdb.Requests.Count);

        // Another user keeps their own budget.
        var other = CreateController(host, Guid.NewGuid());
        Assert.NotNull((await other.Suggest("matrix", [], null, null, CancellationToken.None)).Value);
    }

    [Fact]
    public async Task GetMovie_TmdbDown_Returns503WithTheSourceStatusOnly()
    {
        using var host = new CatalogTestHost(tmdb: (_, _) => Task.FromResult(FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable)));
        var controller = CreateController(host, Guid.NewGuid());

        var response = await controller.GetMovie(550, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(new CatalogSourceReport(CatalogSource.Tmdb, CatalogSourceStatus.Unavailable), result.Value);
    }

    [Fact]
    public void EveryEndpoint_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(CatalogController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.DoesNotContain(
            typeof(CatalogController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null);
    }
}
