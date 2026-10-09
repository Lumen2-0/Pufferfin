using System;
using SkiaSharp;

namespace Pufferfin.Server.Users;

/// <summary>
/// Draws the picture a new account starts with: soft blobs of a warm palette melting into each other, as a
/// gradient. The palette and the shapes come from the account id, so each account has its own, always the same.
/// </summary>
public static class DefaultAvatar
{
    /// <summary>
    /// The side of the picture, in pixels.
    /// </summary>
    public const int Size = 512;

    // Warm, green and violet palettes, never blue: the colors of the interface
    private static readonly string[][] _palettes =
    [
        ["#FF7A59", "#FFB36B", "#E0457B"],
        ["#F6C6A8", "#E98973", "#B85C5C"],
        ["#C6F16D", "#4FBF7A", "#1F7A5A"],
        ["#E7A3D7", "#B05FD0", "#F27E9E"],
        ["#FFD166", "#F4A259", "#8C5A3C"],
        ["#FF9AA2", "#FFD6C9", "#E2425D"],
        ["#B8E0C8", "#E8D9B5", "#6FB98F"],
        ["#FF5F40", "#FFB03A", "#7A1F2B"],
        ["#D9C2F0", "#F3A6C8", "#7E4FA8"],
        ["#F2E8CF", "#BC6C25", "#606C38"]
    ];

    /// <summary>
    /// Draws the picture of an account.
    /// </summary>
    /// <param name="seed">The account id.</param>
    /// <returns>The picture, as a PNG file.</returns>
    public static byte[] Render(Guid seed)
    {
        var bytes = seed.ToByteArray();
#pragma warning disable CA5394 // Not a secret: a stable look per account.
        var random = new Random(BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 8));
        var palette = Array.ConvertAll(_palettes[random.Next(_palettes.Length)], SKColor.Parse);

        using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // The ground: two colors of the palette across the picture, at an angle of its own
        var angle = random.NextDouble() * Math.PI * 2;
        var dx = (float)Math.Cos(angle) * Size / 2;
        var dy = (float)Math.Sin(angle) * Size / 2;
        const float Center = Size / 2f;
        using (var ground = new SKPaint())
        {
            ground.Shader = SKShader.CreateLinearGradient(
                new SKPoint(Center - dx, Center - dy),
                new SKPoint(Center + dx, Center + dy),
                [palette[0], palette[1]],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Size, Size, ground);
        }

        // Three soft blobs, each color once, fading into the ground
        for (var i = 0; i < 3; i++)
        {
            var color = palette[(i + 2) % palette.Length];
            var center = new SKPoint((float)(random.NextDouble() * Size), (float)(random.NextDouble() * Size));
            var radius = (float)(Size * (0.35 + (random.NextDouble() * 0.35)));
            using var blob = new SKPaint { IsAntialias = true };
            blob.Shader = SKShader.CreateRadialGradient(
                center,
                radius,
                [color.WithAlpha(230), color.WithAlpha(0)],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Size, Size, blob);
        }
#pragma warning restore CA5394

        // A faint light from the top, for depth
        using (var light = new SKPaint())
        {
            light.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, Size),
                [SKColors.White.WithAlpha(40), SKColors.White.WithAlpha(0)],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Size, Size, light);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
