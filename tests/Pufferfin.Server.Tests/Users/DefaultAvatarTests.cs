using System;
using Pufferfin.Server.Users;
using SkiaSharp;
using Xunit;

namespace Pufferfin.Server.Tests.Users;

public sealed class DefaultAvatarTests
{
    private static readonly bool _hasSkia = HasSkia();

    [Fact]
    public void Render_DrawsASquarePng()
    {
        Assert.SkipUnless(_hasSkia, "The native Skia library is not installed here.");
        var png = DefaultAvatar.Render(Guid.NewGuid());

        using var bitmap = SKBitmap.Decode(png);
        Assert.Equal(DefaultAvatar.Size, bitmap.Width);
        Assert.Equal(DefaultAvatar.Size, bitmap.Height);
    }

    [Fact]
    public void Render_IsTheSame_ForOneAccount_AndDiffers_BetweenAccounts()
    {
        Assert.SkipUnless(_hasSkia, "The native Skia library is not installed here.");
        var id = Guid.Parse("2f1a5b8e-7c34-4a1d-9e0b-55c6d1f0a3b2");

        Assert.Equal(DefaultAvatar.Render(id), DefaultAvatar.Render(id));
        Assert.NotEqual(DefaultAvatar.Render(id), DefaultAvatar.Render(Guid.Parse("8d0e4c21-1b6f-4f7a-a2c9-0e93b7d45f61")));
    }

    private static bool HasSkia()
    {
        try
        {
            using var bitmap = new SKBitmap(1, 1);
            return true;
        }
        catch (TypeInitializationException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
