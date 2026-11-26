using OpenIGI.Core.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Skia;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The remastered crosshair in the styles a player may pick, side by side, to look at.
/// Opt-in: set <c>OPENIGI_SHELL_SHOTS</c> to a folder and run this test; it writes <c>crosshairs.png</c>.
/// </summary>
public sealed class CrosshairScreenshots
{
    [Fact]
    public void Draw_the_crosshair_styles()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        (string Name, CrosshairStyle Style)[] styles =
        [
            ("default", new CrosshairStyle(Bgra32.FromRgba(255, 255, 255, 235), 1f, 1f, 0f, false, true, true)),
            ("green dot", new CrosshairStyle(HudCrosshair.ColourAt(1).WithAlpha(235), 1f, 1f, 0f, true, true, true)),
            ("cyan large", new CrosshairStyle(HudCrosshair.ColourAt(3).WithAlpha(235), 2f, 1.6f, 4f, false, true, true)),
            ("yellow static", new CrosshairStyle(HudCrosshair.ColourAt(2).WithAlpha(235), 1.4f, 1.2f, 2f, true, false, false)),
            ("magenta thin", new CrosshairStyle(HudCrosshair.ColourAt(4).WithAlpha(200), 1.2f, 0.6f, 0f, false, true, true)),
            ("orange", new CrosshairStyle(HudCrosshair.ColourAt(6).WithAlpha(235), 1.5f, 1.4f, 1f, true, true, true)),
        ];

        const int Cell = 200;
        int width = Cell * styles.Length;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, Cell, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, Cell), [new SKColor(150, 140, 120), new SKColor(40, 44, 40)], SKShaderTileMode.Clamp) };
        surface.Canvas.DrawRect(0, 0, width, Cell, sky);
        using SkiaImageStore images = new();
        using SkiaFontCache fonts = new();
        using (SkiaUiRenderer renderer = new(surface.Canvas, images, fonts))
        {
            var painter = new UiPainter(renderer, new LayoutContext(width, Cell));
            for (int i = 0; i < styles.Length; i++)
            {
                // Each at 2x, a rifle's spread a little open.
                HudCrosshair.Paint(painter, new UiVec2((i * Cell) + (Cell / 2f), Cell / 2f), 2f, CrosshairShape.Rifle, 14f, styles[i].Style);
            }

            renderer.Flush();
        }

        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
        using FileStream file = File.Create(Path.Combine(folder, "crosshairs.png"));
        png.SaveTo(file);
    }
}
