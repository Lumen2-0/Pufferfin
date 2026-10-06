using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Api;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.Arr;
using Pufferfin.Server.AutoConfig;
using Pufferfin.Server.AutoConfig.Steps;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Requests;
using Pufferfin.Server.Tests.AutoConfig;
using Pufferfin.Server.Tests.TestHelpers;
using Xunit;

namespace Pufferfin.Server.Tests.Requests;

public sealed class WebhookTests : IDisposable
{
    private readonly RequestTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private (PufferfinWebhookController Controller, Waker Waker) Controller(string? authorization)
    {
        var waker = new Waker();
        var controller = new PufferfinWebhookController(_kit.Store, waker, _kit.Library, NullLogger<PufferfinWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (authorization is not null)
        {
            controller.Request.Headers.Authorization = authorization;
        }

        return (controller, waker);
    }

    private static string Basic(string password) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(NotificationStep.WebhookUser + ":" + password));

    [Theory]
    [InlineData(null)]
    [InlineData("Basic !!!")]
    [InlineData("MediaBrowser Token=\"abc\"")]
    public void AWebhookWithoutTheToken_IsRefused_AndDoesNothing(string? authorization)
    {
        var (controller, waker) = Controller(authorization);

        var result = controller.ReceivePufferfinArrNotification(new JsonObject { ["eventType"] = "Download" });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(0, waker.Count);
        Assert.Empty(_kit.Library.Refreshed);
    }

    [Fact]
    public void AWebhookWithAWrongToken_IsRefused()
    {
        var (controller, _) = Controller(Basic("wrong"));

        Assert.IsType<UnauthorizedResult>(controller.ReceivePufferfinArrNotification(new JsonObject { ["eventType"] = "Test" }));
    }

    [Fact]
    public void AnImportNotification_RefreshesTheLibrary_AndWakesTheProcessor()
    {
        var (controller, waker) = Controller(Basic(_kit.Store.GetWebhookToken()));

        var result = controller.ReceivePufferfinArrNotification((JsonObject)JsonNode.Parse("{\"eventType\":\"Download\",\"movie\":{\"folderPath\":\"/media/movies/Dune (2021)\"},\"movieFile\":{\"path\":\"/media/movies/Dune (2021)/Dune.mkv\"}}")!);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(["/media/movies/Dune (2021)/Dune.mkv"], _kit.Library.Refreshed);
        Assert.Equal(1, waker.Count);
    }

    [Fact]
    public void TheWebhookController_IsTheOnlyAnonymousOne_AndTheRequestsAdministrationNeedsElevation()
    {
        Assert.NotNull(typeof(PufferfinWebhookController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(typeof(PufferfinRequestsController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(typeof(PufferfinRequestsController).GetCustomAttribute<AuthorizeAttribute>());
        foreach (var name in new[] { "ApprovePufferfinRequest", "RejectPufferfinRequest", "MovePufferfinRequest", "GetPufferfinRequestPermissions", "SetPufferfinRequestPermissions" })
        {
            Assert.Equal(Policies.RequiresElevation, typeof(PufferfinRequestsController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        }
    }

    [Fact]
    public async Task TheNotificationStep_DeclaresTheWebhookOnce_WithTheTokenAsBasicPassword()
    {
        var server = ArrFixtures.RadarrLike();
        server.Resources["notification"] = [];
        server.Schemas["notification"] = JsonNode.Parse("""
            [{"implementation":"Webhook","name":"","onGrab":false,"onDownload":false,"onUpgrade":false,
              "fields":[{"name":"url","value":""},{"name":"method","value":1},{"name":"username","value":""},{"name":"password","value":""}]}]
            """)!;
        var step = new NotificationStep(new ArrClient(FakeArrServer.CreateHttp(server)), PufferfinService.Radarr, _kit.Store, () => new Uri("http://pufferfin:8096/"));

        var first = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);
        var second = await step.ApplyAsync(ArrFixtures.FullStack(), CancellationToken.None);

        Assert.Equal(AutoConfigStepStatus.Applied, first.Status);
        Assert.Equal(AutoConfigStepStatus.Unchanged, second.Status);
        var notification = Assert.Single(server.Resource("notification"))!.AsObject();
        Assert.Equal("http://pufferfin:8096/Pufferfin/Requests/Webhook", ArrFields.Get(notification, "url")!.GetValue<string>());
        Assert.Equal(_kit.Store.GetWebhookToken(), ArrFields.Get(notification, "password")!.GetValue<string>());
        Assert.True(notification["onDownload"]!.GetValue<bool>());
        Assert.True(notification["onGrab"]!.GetValue<bool>());
        Assert.DoesNotContain(_kit.Store.GetWebhookToken(), first.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequest_AsTheClientSeesIt_CarriesNoSecretNorToken()
    {
        var request = RequestTestKit.Request() with
        {
            State = RequestState.Failed,
            Problem = new RequestProblem(RequestProblemCode.ServiceUnavailable, "Radarr could not be reached.")
        };

        var json = System.Text.Json.JsonSerializer.Serialize(MediaRequestDto.From(request, null));

        Assert.DoesNotContain(_kit.Store.GetWebhookToken(), json, StringComparison.Ordinal);
        Assert.DoesNotContain(ArrFixtures.RadarrKey, json, StringComparison.Ordinal);
        var names = typeof(MediaRequestDto).GetProperties().Select(p => p.Name.ToUpperInvariant());
        Assert.DoesNotContain(names, n => n.Contains("KEY", StringComparison.Ordinal) || n.Contains("TOKEN", StringComparison.Ordinal) || n.Contains("PASSWORD", StringComparison.Ordinal));
    }

    private sealed class Waker : IRequestWaker
    {
        public int Count { get; private set; }

        public void Wake() => Count++;
    }
}
