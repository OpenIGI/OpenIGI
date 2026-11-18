using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Hud;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Skia;
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
                    AllyTag.Draw(painter, typography.Body, new UiVec2(width * x, height * y), name, metres);
                }

                renderer.Flush();
            }

            using SKImage image = surface.Snapshot();
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
            using FileStream file = File.Create(Path.Combine(folder, $"allies-{width}x{height}.png"));
            png.SaveTo(file);
        }
    }
}
