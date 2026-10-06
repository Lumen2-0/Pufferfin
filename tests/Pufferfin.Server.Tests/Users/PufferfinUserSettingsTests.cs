using System;
using System.IO;
using System.Security.Claims;
using Jellyfin.Api.Constants;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Api;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.Catalog;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Tests.TestHelpers;
using Pufferfin.Server.Users;
using Xunit;

namespace Pufferfin.Server.Tests.Users;

/// <summary>
/// The Letterboxd account is optional and belongs to each user, not to the admin panel.
/// </summary>
public sealed class PufferfinUserSettingsTests : IDisposable
{
    private static readonly Guid _alice = Guid.NewGuid();
    private static readonly Guid _bob = Guid.NewGuid();

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));
    private readonly CapturingLoggerProvider _logs = new();

    private string FilePath => Path.Combine(_directory, "pufferfin-users.json");

    private PufferfinUserSettingsStore Start() => new(FilePath, _logs.CreateLogger<PufferfinUserSettingsStore>());

    [Fact]
    public void LetterboxdAccount_IsOptional_PerUser_AndSurvivesARestart()
    {
        var store = Start();
        Assert.Null(store.Get(_alice).LetterboxdUsername);

        store.Set(_alice, new PufferfinUserSettings { LetterboxdUsername = " alice_films " });

        var restarted = Start();
        Assert.Equal("alice_films", restarted.Get(_alice).LetterboxdUsername);
        Assert.Null(restarted.Get(_bob).LetterboxdUsername);
        Assert.Equal("alice_films", new StoreLetterboxdAccountProvider(restarted).GetUsername(_alice));
        Assert.Null(new StoreLetterboxdAccountProvider(restarted).GetUsername(_bob));
    }

    [Fact]
    public void BlankUsername_UnlinksTheAccount()
    {
        var store = Start();
        store.Set(_alice, new PufferfinUserSettings { LetterboxdUsername = "alice_films" });

        store.Set(_alice, new PufferfinUserSettings { LetterboxdUsername = "  " });

        Assert.Null(Start().Get(_alice).LetterboxdUsername);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("with space")]
    [InlineData("../../etc")]
    [InlineData("waytoolongusername")]
    public void InvalidUsername_IsRejected_AndNothingIsSaved(string username)
    {
        var store = Start();
        store.Set(_alice, new PufferfinUserSettings { LetterboxdUsername = "alice_films" });

        var ex = Assert.Throws<SettingsValidationException>(() => store.Set(_alice, new PufferfinUserSettings { LetterboxdUsername = username }));

        Assert.Contains(PufferfinUserSettingsStore.LetterboxdUsernameKey, ex.Errors.Keys);
        Assert.Equal("alice_films", Start().Get(_alice).LetterboxdUsername);
    }

    [Fact]
    public void Controller_ReadsAndWritesOnlyTheCurrentUser()
    {
        var store = Start();
        store.Set(_bob, new PufferfinUserSettings { LetterboxdUsername = "bob_watches" });
        var controller = ControllerFor(store, _alice);

        Assert.Null(controller.GetPufferfinUserSettings().Value!.LetterboxdUsername);
        var saved = controller.UpdatePufferfinUserSettings(new PufferfinUserSettingsDto { LetterboxdUsername = "alice_films" });

        Assert.Equal("alice_films", saved.Value!.LetterboxdUsername);
        Assert.Equal("alice_films", store.Get(_alice).LetterboxdUsername);
        Assert.Equal("bob_watches", store.Get(_bob).LetterboxdUsername);
        Assert.IsType<BadRequestObjectResult>(controller.UpdatePufferfinUserSettings(new PufferfinUserSettingsDto { LetterboxdUsername = "not valid!" }).Result);
    }

    [Fact]
    public void Controller_IsOpenToEveryAuthenticatedUser_NotOnlyAdministrators()
    {
        var authorize = Assert.Single(typeof(PufferfinUserSettingsController).GetCustomAttributes(typeof(AuthorizeAttribute), false));
        Assert.NotEqual(Policies.RequiresElevation, ((AuthorizeAttribute)authorize).Policy);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static PufferfinUserSettingsController ControllerFor(IPufferfinUserSettingsStore store, Guid userId)
        => new(store)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, userId.ToString("N"))], "Test"))
                }
            }
        };
}
