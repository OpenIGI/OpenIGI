using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Hud;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Widgets;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Allies' name tags (<see cref="AllyTag"/>) over a level's picture, near and far, to look at.
/// Opt-in: set <c>OPENIGI_SHELL_SHOTS</c> to a folder and run this test; it writes <c>allies-&lt;width&gt;x&lt;height&gt;.png</c>.
/// </summary>
public sealed class AllyTagScreenshots
{
    [Fact]
    public void Draw_allies_name_tags()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using IgiTypography typography = IgiTypography.Load(MultiplayerScreenshots.LoadFont);
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();
        UiPicture? level = MultiplayerScreenshots.Picture(images, [], 1);

        foreach ((int width, int height) in new[] { (1920, 1080), (1280, 720) })
        {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            MultiplayerScreenshots.DrawGround(surface.Canvas, width, height);
            using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
            {
                var painter = new UiPainter(renderer, new LayoutContext(width, height));
                level?.DrawCover(painter, new UiRect(0f, 0f, width, height));

                // Allies near and far: a teammate at arm's length, one across the yard, two across the compound.
                (float X, float Y, string Name, double Metres)[] allies =
                [
                    (0.30f, 0.58f, "Anya", 6.0),
                    (0.52f, 0.50f, "Jones", 18.0),
                    (0.68f, 0.44f, "Priboi", 42.0),
                    (0.84f, 0.40f, "Ekk", 96.0),
                ];
                foreach ((float x, float y, string name, double metres) in allies)
                {
                    // Jones is hurt: his tag carries a thin bar of what is left.
                    AllyTag.Draw(painter, typography.Body, new UiVec2(width * x, height * y), name, metres, name == "Jones" ? 0.45f : 1f);
                }

                renderer.Flush();
            }

            using SKImage image = surface.Snapshot();
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
            using FileStream file = File.Create(Path.Combine(folder, $"allies-{width}x{height}.png"));
            png.SaveTo(file);
        }
    }

    [Fact]
    public void Draw_the_revive_hud()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using IgiTypography typography = IgiTypography.Load(MultiplayerScreenshots.LoadFont);
        UiTheme theme = typography.CreateTheme();
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();
        UiPicture? level = MultiplayerScreenshots.Picture(images, [], 1);

        // A teammate down and waiting, one being brought back; and this player down, then being brought back.
        foreach ((string state, float progress) in new[] { ("down", 0f), ("reviving", 0.45f) })
        {
            const int Width = 1920;
            const int Height = 1080;
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            MultiplayerScreenshots.DrawGround(surface.Canvas, Width, Height);
            using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
            {
                var painter = new UiPainter(renderer, new LayoutContext(Width, Height));
                level?.DrawCover(painter, new UiRect(0f, 0f, Width, Height));
                ReviveHud.DrawMarker(painter, theme, new UiVec2(Width * 0.36f, Height * 0.56f), "Anya", 0f, 23);
                ReviveHud.DrawMarker(painter, theme, new UiVec2(Width * 0.62f, Height * 0.48f), "Jones", 0.6f, 4);

                // One out of view to the right: pinned to the edge, pointing the way.
                ReviveHud.DrawMarker(painter, theme, new UiVec2(Width - 64f, Height * 0.42f), "Priboi", 0f, 118, new UiVec2(1f, -0.15f));
                ReviveHud.DrawAlert(painter, theme, 0, "Priboi", 118, 1f);
                ReviveHud.DrawDowned(painter, theme, progress);
                renderer.Flush();
            }

            using SKImage image = surface.Snapshot();
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
            using FileStream file = File.Create(Path.Combine(folder, $"revive-{state}-{Width}x{Height}.png"));
            png.SaveTo(file);
        }
    }

    [Fact]
    public void Draw_pings()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using IgiTypography typography = IgiTypography.Load(MultiplayerScreenshots.LoadFont);
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();
        UiPicture? level = MultiplayerScreenshots.Picture(images, [], 1);
        const int Width = 1920;
        const int Height = 1080;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        MultiplayerScreenshots.DrawGround(surface.Canvas, Width, Height);
        using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
        {
            var painter = new UiPainter(renderer, new LayoutContext(Width, Height));
            level?.DrawCover(painter, new UiRect(0f, 0f, Width, Height));

            // One ping at three moments of its landing, one settled, and one out of view waiting on the edge.
            PingMark.Draw(painter, typography.Body, new UiVec2(Width * 0.30f, Height * 0.62f), "Anya", 31, 0.08f, 1f);
            PingMark.Draw(painter, typography.Body, new UiVec2(Width * 0.42f, Height * 0.62f), "Anya", 31, 0.35f, 1f);
            PingMark.Draw(painter, typography.Body, new UiVec2(Width * 0.54f, Height * 0.62f), "Anya", 31, 0.7f, 1f);
            PingMark.Draw(painter, typography.Body, new UiVec2(Width * 0.66f, Height * 0.42f), "Jones", 74, 4f, 1f);
            PingMark.Draw(painter, typography.Body, new UiVec2(64f, Height * 0.5f), "Priboi", 140, 4f, 1f, new UiVec2(-1f, 0.1f));
            renderer.Flush();
        }

        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
        using FileStream file = File.Create(Path.Combine(folder, $"pings-{Width}x{Height}.png"));
        png.SaveTo(file);
    }
}
