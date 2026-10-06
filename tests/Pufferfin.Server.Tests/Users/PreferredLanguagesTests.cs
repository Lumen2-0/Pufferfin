using System;
using System.IO;
using System.Security.Claims;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Pufferfin.Server.Api;
using Pufferfin.Server.Api.Models;
using Pufferfin.Server.Configuration;
using Pufferfin.Server.Users;
using Xunit;

namespace Pufferfin.Server.Tests.Users;

public sealed class PreferredLanguagesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pufferfin-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid _user = Guid.NewGuid();

    public PreferredLanguagesTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private PufferfinUserSettingsStore Start() => new(Path.Combine(_directory, "users.json"), NullLogger<PufferfinUserSettingsStore>.Instance);

    [Fact]
    public void PreferredLanguages_AreSavedInOrder_WithoutDuplicates_AndSurviveARestart()
    {
        Start().Set(_user, new PufferfinUserSettings { PreferredAudioLanguages = ["fr", "en", "FR", "pt-BR"] });

        Assert.Equal(["fr", "en", "pt-BR"], Start().Get(_user).PreferredAudioLanguages);
    }

    [Fact]
    public void AnInvalidLanguage_IsRefused_AndNothingIsSaved()
    {
        var store = Start();
        store.Set(_user, new PufferfinUserSettings { PreferredAudioLanguages = ["fr"] });

        var ex = Assert.Throws<SettingsValidationException>(() => store.Set(_user, new PufferfinUserSettings { PreferredAudioLanguages = ["fr", "not a language"] }));

        Assert.Contains(PufferfinUserSettingsStore.PreferredAudioLanguagesKey, ex.Errors.Keys);
        Assert.Equal(["fr"], Start().Get(_user).PreferredAudioLanguages);
    }

    [Fact]
    public void AClientThatDoesNotSendTheLanguages_KeepsThem_AndAnEmptyListClearsThem()
    {
        var store = Start();
        store.Set(_user, new PufferfinUserSettings { PreferredAudioLanguages = ["de"] });
        var controller = new PufferfinUserSettingsController(store)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalClaimTypes.UserId, _user.ToString("N"))], "Test")) }
            }
        };

        controller.UpdatePufferfinUserSettings(new PufferfinUserSettingsDto { LetterboxdUsername = "cinephile" });
        Assert.Equal(["de"], store.Get(_user).PreferredAudioLanguages);

        controller.UpdatePufferfinUserSettings(new PufferfinUserSettingsDto { LetterboxdUsername = "cinephile", PreferredAudioLanguages = [] });
        Assert.Empty(store.Get(_user).PreferredAudioLanguages);
    }
}
