using System;
using System.Collections.Generic;
using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using Pufferfin.Server.Deletion;
using Xunit;

namespace Pufferfin.Server.Tests.Seeding;

/// <summary>
/// The library lookup of the deletion goes through the user's access for anyone but an administrator.
/// </summary>
public sealed class JellyfinLibraryContentTests
{
    private readonly List<(string Name, object?[]? Arguments)> _libraryCalls = [];

    [Fact]
    public void UnknownUser_FindsNothing_AndTheLibraryIsNotAsked()
    {
        var content = Content(user: null);

        Assert.Null(content.Find(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Empty(_libraryCalls);
    }

    [Fact]
    public void KnownUser_IsLookedUpWithTheirAccess()
    {
        var user = new User("kid", "provider", "reset");
        var itemId = Guid.NewGuid();

        Assert.Null(Content(user).Find(itemId, user.Id));

        var call = Assert.Single(_libraryCalls);
        Assert.Equal(nameof(ILibraryManager.GetItemById), call.Name);
        Assert.Equal(new object?[] { itemId, user }, call.Arguments);
    }

    [Fact]
    public void Administrator_IsLookedUpWithoutRestriction()
    {
        var itemId = Guid.NewGuid();

        Assert.Null(Content(user: null).Find(itemId, visibleTo: null));

        Assert.Equal(new object?[] { itemId }, Assert.Single(_libraryCalls).Arguments);
    }

    private JellyfinLibraryContent Content(User? user)
        => new(
            Stub<ILibraryManager>((method, arguments) =>
            {
                _libraryCalls.Add((method.Name, arguments));
                return null;
            }),
            Stub<IUserManager>((method, _) => method.Name == nameof(IUserManager.GetUserById) ? user : null));

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> answer)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, SeedingRegistrationTests.StubProxy>();
        ((SeedingRegistrationTests.StubProxy)(object)proxy).Answer = answer;
        return proxy;
    }
}
