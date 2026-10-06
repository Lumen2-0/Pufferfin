using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pufferfin.Server.Api;
using Xunit;

namespace Pufferfin.Server.Tests.Api;

/// <summary>
/// Access is enforced by the server through Jellyfin authorization policies, never only hidden in the UI.
/// The end to end check (a non administrator gets 403) lives in Jellyfin.Server.Integration.Tests.
/// </summary>
public sealed class AuthorizationAttributeTests
{
    [Fact]
    public void ConfigurationController_RequiresAnAdministrator_OnEveryAction()
    {
        var type = typeof(PufferfinConfigurationController);

        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(Policies.RequiresElevation, authorize.Policy);

        foreach (var action in Actions(type))
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());

            // An action level policy would be combined, never looser; still, keep a single source of truth.
            Assert.Null(action.GetCustomAttribute<AuthorizeAttribute>());
        }
    }

    [Fact]
    public void EveryPufferfinController_RequiresAuthentication()
    {
        var controllers = typeof(PufferfinConfigurationController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var controller in controllers)
        {
            Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.All(Actions(controller), a => Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>()));
        }
    }

    private static MethodInfo[] Actions(System.Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
}
