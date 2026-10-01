using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Widgets;
using SkiaSharp;

namespace OpenIGI.Launcher;

/// <summary>The decoded backdrop picture, with its width over its height.</summary>
internal readonly record struct BackdropPicture(UiImage Image, float Aspect);

/// <summary>
/// The launcher's background: a night shot of the game under the design's vignette.
/// </summary>
/// <remarks>
/// The picture is drawn sharp; the glass panels in front frost it where they sit, as they do any backdrop.
/// It fills the window the way CSS <c>background-size: cover</c> does, slightly overscanned so it can drift
/// sideways when the game changes, the way the mock-up's framing does. Without the picture the background
/// falls back to a gradient in the same tones.
/// </remarks>
internal sealed class LauncherBackdrop(BackdropPicture? picture) : UiElement
{
    /// <summary>How far past the window the picture is drawn, which is how far it can drift.</summary>
    private const float Overscan = 1.06f;

    private readonly UI.Animation.Spring _focus = new(0.2f, smoothTime: 0.4f);

    /// <summary>Gets the picture's file, beside the launcher.</summary>
    public static string PicturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "backdrop.webp");

    /// <summary>Gets or sets where the picture is framed across its width, 0 to 1.</summary>
    public float Focus { get; set; } = 0.2f;

    /// <summary>Decodes the backdrop picture.</summary>
    /// <param name="images">The store the picture is registered in.</param>
    /// <returns>The picture; <see langword="null"/> when it is missing or cannot be decoded.</returns>
    public static BackdropPicture? LoadPicture(SkiaImageStore images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (!File.Exists(PicturePath))
        {
            return null;
        }

        SKImage? picture = SKImage.FromEncodedData(PicturePath);
        return picture is null ? null : new BackdropPicture(images.Add(picture), picture.Width / (float)picture.Height);
    }

    protected override void OnUpdate(float elapsedSeconds)
    {
        _focus.Target = Focus;
        _focus.Update(elapsedSeconds);
    }

    protected override void OnDraw(UiPainter painter)
    {
        UiRect rect = Bounds;
        if (picture is { } shot)
        {
            DrawPicture(painter, rect, shot);
        }
        else
        {
            DrawGradient(painter, rect);
        }

        KitDraw.Vignette(painter, rect, VignetteStyle.Launcher, glow: false);
    }

    private void DrawPicture(UiPainter painter, UiRect rect, BackdropPicture shot)
    {
        // Cover: whichever of width or height leaves no gap decides the size, then a little overscan.
        float width = MathF.Max(rect.Width, rect.Height * shot.Aspect) * Overscan;
        float height = width / shot.Aspect;
        float x = rect.X - ((width - rect.Width) * Math.Clamp(_focus.Value, 0f, 1f));
        float y = rect.Y - ((height - rect.Height) * 0.5f);
        painter.DrawSprite(shot.Image, new UiRect(x, y, width, height), new UiRect(0f, 0f, 1f, 1f), Core.Graphics.Bgra32.FromRgb(255, 255, 255));

        // A light shade over it, so the text in front still reads where the picture is brightest.
        painter.FillRect(rect, UiPalette.Ink.WithAlpha(64));
    }

    private void DrawGradient(UiPainter painter, UiRect rect)
    {
        // radial-gradient(ellipse at 50% 40%, #1C2024 0%, #0B0D0F 70%)
        Span<UiGradientStop> field =
        [
            new(0f, Core.Graphics.Bgra32.FromRgb(28, 32, 36)),
            new(0.7f, Core.Graphics.Bgra32.FromRgb(11, 13, 15)),
            new(1f, Core.Graphics.Bgra32.FromRgb(11, 13, 15)),
        ];
        painter.FillRadialGradient(rect, new UiVec2(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.4f)), new UiVec2(rect.Width * 0.75f, rect.Height * 0.75f), field);

        // The warm haze: where the picture's light would be.
        Span<UiGradientStop> haze =
        [
            new(0f, UiPalette.AccentAt(0.16f)),
            new(0.45f, UiPalette.AccentAt(0.05f)),
            new(1f, UiPalette.AccentAt(0f)),
        ];
        var center = new UiVec2(rect.X + (rect.Width * (0.35f + (0.4f * _focus.Value))), rect.Y + (rect.Height * 0.42f));
        painter.FillRadialGradient(rect, center, new UiVec2(rect.Width * 0.55f, rect.Height * 0.6f), haze);

        Span<UiGradientStop> cool =
        [
            new(0f, Core.Graphics.Bgra32.FromRgba(80, 110, 130, 46)),
            new(1f, Core.Graphics.Bgra32.FromRgba(80, 110, 130, 0)),
        ];
        painter.FillRadialGradient(rect, new UiVec2(rect.X + (rect.Width * 0.85f), rect.Y + (rect.Height * 0.25f)), new UiVec2(rect.Width * 0.45f, rect.Height * 0.5f), cool);
    }
}
