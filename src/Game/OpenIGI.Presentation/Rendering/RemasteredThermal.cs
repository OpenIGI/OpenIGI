using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Rendering;

/// <summary>What the remastered thermal eyepiece shows this frame.</summary>
/// <param name="HeadingRadians">Where the player is looking, for the compass strip.</param>
/// <param name="Magnification">How many times the view is magnified against the unzoomed one.</param>
/// <param name="Palette">How the sensor colours heat.</param>
/// <param name="Calibration">How far through its calibration the sensor is: 1 as it comes up, 0 once settled.</param>
/// <param name="PaletteNotice">How strongly the palette's name shows: 1 as it is changed, fading to 0.</param>
/// <param name="Seconds">The render clock, for what blinks.</param>
public readonly record struct RemasteredThermalView(
    float HeadingRadians,
    float Magnification,
    ThermalPalette Palette,
    float Calibration,
    float PaletteNotice,
    float Seconds);

/// <summary>The remastered thermal goggles' eyepiece: the vector interface over the sensor's picture.</summary>
/// <remarks>
/// <b>Not retail.</b> The original lays its <c>infravision.spr</c> frame over the infrared view. The remaster's sensor
/// (<see cref="ThermalDisplaySettings"/>) owns the picture and its vignette; this draws the crisp chrome over it, in the shared
/// optics frame (<see cref="OpticsChrome"/>): the mode and palette, the heat scale in the palette's own colours, the reticle,
/// the compass strip, the calibration as it runs and the palette's name as it is chosen.
/// </remarks>
public sealed class RemasteredThermal
{
    // The scale's slices: enough that the palette reads as a gradient however it bends.
    private const int ScaleSlices = 24;

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private OpticsFaces? _faces;

    /// <summary>Initialises a new instance of the <see cref="RemasteredThermal"/> class.</summary>
    /// <param name="device">The device the faces are made on.</param>
    /// <param name="logger">Where a face that cannot load is reported.</param>
    public RemasteredThermal(IGraphicsDevice device, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the faces loaded, so the interface can draw.</summary>
    public bool IsReady => _faces is not null;

    /// <summary>Loads the interface's faces, the menus' own.</summary>
    /// <returns>Whether they loaded.</returns>
    public bool Load()
    {
        _faces = OpticsFaces.Load(_device, _logger, "the remastered thermal goggles");
        return _faces is not null;
    }

    /// <summary>Draws the interface into an open batch.</summary>
    /// <param name="batch">The open batch.</param>
    /// <param name="view">What it shows.</param>
    /// <param name="screenWidth">The target's width in pixels.</param>
    /// <param name="screenHeight">The target's height in pixels.</param>
    public void Draw(SpriteBatch batch, in RemasteredThermalView view, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_faces is not { } faces)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);

        UiRect frame = OpticsChrome.DrawFrame(painter, context, Chrome);
        HudCompassStrip.Draw(painter, context, faces.Theme, view.HeadingRadians);

        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float centreY = context.SafeRect.Y + (context.SafeRect.Height * 0.5f);
        OpticsChrome.DrawReticle(painter, context, centreX, centreY, Chrome.WithAlpha(210), Hot);

        // The mode, top left, with the band the sensor sees in.
        float label = context.Px(15f);
        UiVectors.DrawTracked(
            painter, faces.Body, $"THERMAL // {ThermalPaletteRamp.Mark(view.Palette)}",
            new UiVec2(frame.X + context.Px(18f), frame.Y + context.Px(18f)), label, Chrome, context.Px(2f));
        painter.DrawText(
            faces.Body, "LWIR  8-14µm  //  GAIN AUTO",
            new UiVec2(frame.X + context.Px(18f), frame.Y + context.Px(42f)), context.Px(12f), Dim);

        // The zoom, bottom left.
        float magnification = float.IsFinite(view.Magnification) && view.Magnification > 0f ? view.Magnification : 1f;
        UiVectors.DrawTracked(
            painter, faces.Body, string.Create(CultureInfo.InvariantCulture, $"x{magnification:0.0}"),
            new UiVec2(frame.X + context.Px(18f), frame.Bottom - context.Px(48f)), label, Chrome, context.Px(2f));

        DrawScale(painter, context, faces, frame, view.Palette);

        if (view.Calibration > 0f)
        {
            DrawCalibration(painter, context, faces, centreX, centreY, view);
        }

        if (view.PaletteNotice > 0f)
        {
            DrawPaletteNotice(painter, context, faces, centreX, frame.Bottom - context.Px(110f), view);
        }
    }

    /// <summary>The heat scale down the right of the eyepiece: the palette's colours, hot at the top, with ticks and its ends named.</summary>
    private static void DrawScale(UiPainter painter, LayoutContext context, OpticsFaces faces, UiRect frame, ThermalPalette palette)
    {
        float width = context.Px(10f);
        float height = MathF.Min(context.Px(260f), frame.Height * 0.45f);
        float x = frame.Right - context.Px(30f) - width;
        float y = frame.Y + ((frame.Height - height) * 0.5f);

        // A dark backing a pixel wider, so the scale reads over any picture.
        float pad = MathF.Max(1f, context.Px(1.5f));
        painter.FillRect(new UiRect(x - pad, y - pad, width + (pad * 2f), height + (pad * 2f)), Bgra32.FromRgba(0, 0, 0, 150));

        float slice = height / ScaleSlices;
        for (int i = 0; i < ScaleSlices; i++)
        {
            float temperature = 1f - ((i + 0.5f) / ScaleSlices);
            painter.FillRect(new UiRect(x, y + (i * slice), width, MathF.Ceiling(slice)), ThermalPaletteRamp.At(palette, temperature));
        }

        // Five ticks, the ends longer.
        float weight = MathF.Max(1f, context.Px(1f));
        for (int tick = 0; tick <= 4; tick++)
        {
            float length = context.Px(tick is 0 or 4 ? 8f : 5f);
            float at = y + (height * tick / 4f);
            painter.FillRect(new UiRect(x - length - context.Px(3f), at - (weight * 0.5f), length, weight), Chrome.WithAlpha(190));
        }

        float size = context.Px(11f);
        float hotWidth = faces.Body.MeasureWidth("HOT", size);
        painter.DrawText(faces.Body, "HOT", new UiVec2(x + (width * 0.5f) - (hotWidth * 0.5f), y - context.Px(20f)), size, Chrome);
        float coldWidth = faces.Body.MeasureWidth("COLD", size);
        painter.DrawText(faces.Body, "COLD", new UiVec2(x + (width * 0.5f) - (coldWidth * 0.5f), y + height + context.Px(8f)), size, Dim);
    }

    /// <summary>The calibration as it runs: the shutter's mark blinking under the reticle and a bar filling.</summary>
    private static void DrawCalibration(UiPainter painter, LayoutContext context, OpticsFaces faces, float centreX, float centreY, in RemasteredThermalView view)
    {
        float alpha = Math.Clamp(view.Calibration * 3f, 0f, 1f);
        bool lit = MathF.Floor(view.Seconds * 8f) % 2f == 0f;
        float size = context.Px(14f);
        const string Text = "NUC  //  CALIBRATING";
        float textWidth = faces.Body.MeasureWidth(Text, size);
        Bgra32 colour = (lit ? Hot : Chrome).WithAlpha((byte)(255f * alpha));
        painter.DrawText(faces.Body, Text, new UiVec2(centreX - (textWidth * 0.5f), centreY + context.Px(52f)), size, colour);

        float barWidth = context.Px(140f);
        float barY = centreY + context.Px(76f);
        float barHeight = MathF.Max(1f, context.Px(2f));
        painter.FillRect(new UiRect(centreX - (barWidth * 0.5f), barY, barWidth, barHeight), Chrome.WithAlpha((byte)(70f * alpha)));
        painter.FillRect(
            new UiRect(centreX - (barWidth * 0.5f), barY, barWidth * (1f - view.Calibration), barHeight),
            Hot.WithAlpha((byte)(230f * alpha)));
    }

    /// <summary>The palette's name, centred low on the eyepiece, as it is chosen, with a strip of its colours beneath.</summary>
    private static void DrawPaletteNotice(UiPainter painter, LayoutContext context, OpticsFaces faces, float centreX, float top, in RemasteredThermalView view)
    {
        float alpha = Math.Clamp(view.PaletteNotice * 2f, 0f, 1f);
        string name = ThermalPaletteRamp.Name(view.Palette);
        float size = context.Px(20f);
        UiFont face = faces.Display ?? faces.Body;
        float width = face.MeasureWidth(name, size);
        painter.DrawText(face, name, new UiVec2(centreX - (width * 0.5f), top), size, Chrome.WithAlpha((byte)(255f * alpha)));

        float stripWidth = MathF.Max(width, context.Px(160f));
        float stripY = top + face.LineHeight(size) + context.Px(6f);
        float stripHeight = MathF.Max(2f, context.Px(4f));
        Span<UiGradientStop> stops =
        [
            new(0f, ThermalPaletteRamp.At(view.Palette, 0f).WithAlpha((byte)(255f * alpha))),
            new(0.25f, ThermalPaletteRamp.At(view.Palette, 0.25f).WithAlpha((byte)(255f * alpha))),
            new(0.5f, ThermalPaletteRamp.At(view.Palette, 0.5f).WithAlpha((byte)(255f * alpha))),
            new(0.75f, ThermalPaletteRamp.At(view.Palette, 0.75f).WithAlpha((byte)(255f * alpha))),
            new(1f, ThermalPaletteRamp.At(view.Palette, 1f).WithAlpha((byte)(255f * alpha))),
        ];
        painter.FillGradient(new UiRect(centreX - (stripWidth * 0.5f), stripY, stripWidth, stripHeight), stops, UiGradientDirection.Horizontal);
    }

    // A cool white for the chrome, so it reads over every palette; the reticle's dot and what is live in the sensor's own hot.
    private static Bgra32 Chrome => Bgra32.FromRgb(226, 236, 242);

    private static Bgra32 Dim => Bgra32.FromRgba(226, 236, 242, 150);

    private static Bgra32 Hot => Bgra32.FromRgb(255, 148, 64);
}
