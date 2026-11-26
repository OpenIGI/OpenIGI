using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Igi1.Hud;
using OpenIGI.Presentation.Igi2.Hud;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Headless;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Text;
using SkiaSharp;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The remastered status cluster in each stance, seen and hidden, whole and hurt, in both titles' looks, to look at.
/// Opt-in: set <c>OPENIGI_SHELL_SHOTS</c> to a folder and run this test; it writes <c>status.png</c>.
/// </summary>
public sealed class StatusScreenshots
{
    private const int CellWidth = 260;
    private const int CellHeight = 340;

    [Fact]
    public void Draw_the_status_cluster()
    {
        string? folder = Environment.GetEnvironmentVariable("OPENIGI_SHELL_SHOTS");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        (HudStance Stance, float Health, float? Visibility)[] cases =
        [
            (HudStance.Standing, 1f, 0.2f),
            (HudStance.Running, 0.62f, 0.55f),
            (HudStance.Crouching, 0.35f, 0.9f),
            (HudStance.Prone, 0.18f, 0.05f),
            (HudStance.Flying, 0.8f, null),
            (HudStance.Standing, 0.5f, 0.8f),
        ];
        HudTheme[] themes = [Igi2HudTheme.Theme, Igi1HudTheme.Theme];

        using IgiTypography typography = IgiTypography.Load(LoadFont);
        HudFonts fonts = new(typography.Mono, typography.Display, typography.Heading, typography.Body);
        int width = CellWidth * cases.Length;
        int height = CellHeight * themes.Length;
        using SKSurface sheet = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (int row = 0; row < themes.Length; row++)
        {
            for (int column = 0; column < cases.Length; column++)
            {
                using SKImage cell = Cell(themes[row], fonts, cases[column].Stance, cases[column].Health, cases[column].Visibility);
                sheet.Canvas.DrawImage(cell, column * CellWidth, row * CellHeight);
            }
        }

        using SKImage image = sheet.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
        using FileStream file = File.Create(Path.Combine(folder, "status.png"));
        png.SaveTo(file);
    }

    private static SKImage Cell(HudTheme theme, HudFonts fonts, HudStance stance, float health, float? visibility)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(CellWidth, CellHeight, SKColorType.Rgba8888, SKAlphaType.Premul));

        // A sunlit wall over dark ground, so the glass has something to blur.
        using var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(CellWidth * 0.6f, CellHeight), [new SKColor(168, 150, 118), new SKColor(70, 78, 64), new SKColor(28, 30, 26)], SKShaderTileMode.Clamp) };
        surface.Canvas.DrawRect(0, 0, CellWidth, CellHeight, sky);

        HudModel model = new() { Show = HudParts.Status, Health = health, Stance = stance, Visibility = visibility };
        HudReactiveState reactive = new();
        model.Health = 1f;
        reactive.Update(model, 0f);
        model.Health = health;
        for (float s = 0.05f; s < 10f; s += 0.05f)
        {
            reactive.Update(model, s);
        }

        using SkiaImageStore images = new();
        using SkiaFontCache cache = new();
        using RecordingGraphicsDevice device = new();
        SpriteBatch batch = new(device);
        using (SkiaUiRenderer renderer = new(surface.Canvas, images, cache))
        {
            UiPainter painter = new(renderer, new LayoutContext(CellWidth, CellHeight));

            // Drawn at twice the design size: a 1440-line display.
            float scale = 2f * HudTheme.DesignHeight / theme.AuthoredHeight;
            HudDrawContext context = new(batch, painter, theme, fonts, NoIcons.Instance, reactive, CellWidth, CellHeight, scale);
            HudStatus.Draw(context, model);
            renderer.Flush();
        }

        return surface.Snapshot();
    }

    private static UiFont LoadFont(IgiFontRequest request)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", request.FileName));
        return UiFont.ForMeasurement(FontAtlasBuilder.Build(data, request.PixelHeight), data);
    }

    private sealed class NoIcons : IHudIconSource
    {
        public static NoIcons Instance { get; } = new();

        // The stance figure's room, measured as the title's picture is, though nothing is drawn in it.
        public bool TryMeasure(HudIcon icon, out float width, out float height)
        {
            width = 34f;
            height = 86f;
            return true;
        }

        public void Draw(SpriteBatch batch, HudIcon icon, float x, float y, float scale, Bgra32 colour)
        {
        }
    }
}
