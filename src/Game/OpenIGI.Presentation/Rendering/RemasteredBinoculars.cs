using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Rendering;

/// <summary>What the remastered binoculars show this frame.</summary>
/// <param name="HeadingRadians">Where the player is looking, for the compass strip, in the title's own sense of a turn.</param>
/// <param name="Magnification">How many times the view is magnified against the unzoomed one.</param>
/// <param name="Contacts">The boxes over what the binoculars picked out, in screen pixels.</param>
/// <param name="Seconds">The wall clock, for what bobs and pulses.</param>
/// <param name="RangeMetres">The range to what the centre rests on, or null when nothing is.</param>
public readonly record struct RemasteredBinocularView(
    float HeadingRadians,
    float Magnification,
    IReadOnlyList<EnemyOverlayBox> Contacts,
    float Seconds,
    float? RangeMetres);

/// <summary>The remastered binoculars' interface, for whichever title's player looks through them.</summary>
/// <remarks>
/// <b>Not retail.</b> The glass tint, bloom and bezel vignette are a resolve-time display transform (see
/// <see cref="BinocularDisplaySettings"/>) and the marked bodies are silhouetted over the picture (see
/// <see cref="BinocularMarking"/>); this draws the crisp vector interface on top: thin rails and corner ticks, the compass
/// strip, the rangefinder reticle, a chevron and range over each contact, and the magnification and range. The faithful
/// retail overlays stay each title's own.
/// </remarks>
public sealed class RemasteredBinoculars
{
    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private UiFont? _body;
    private UiFont? _display;
    private UiTheme? _theme;

    /// <summary>Initialises a new instance of the <see cref="RemasteredBinoculars"/> class.</summary>
    /// <param name="device">The device the faces are made on.</param>
    /// <param name="logger">Where a face that cannot load is reported.</param>
    public RemasteredBinoculars(IGraphicsDevice device, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the faces loaded, so the interface can draw.</summary>
    public bool IsReady => _body is not null;

    /// <summary>Loads the interface's faces, the menus' own.</summary>
    /// <returns>Whether they loaded.</returns>
    public bool Load()
    {
        if (!UiFontLibrary.TypographyAvailable)
        {
            _logger.Warning("Hud", $"No interface font at {UiFontLibrary.FontsDirectory}; the remastered binoculars cannot draw.");
            return false;
        }

        try
        {
            IgiTypography typography = UiFontLibrary.Typography(_device);
            _theme = typography.CreateTheme();
            _body = typography.Body;
            _display = typography.Display;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning("Hud", $"Could not load the remastered binoculars' faces: {ex.Message}");
            return false;
        }
    }

    /// <summary>Draws the interface into an open batch.</summary>
    /// <param name="batch">The open batch.</param>
    /// <param name="view">What it shows.</param>
    /// <param name="screenWidth">The target's width in pixels.</param>
    /// <param name="screenHeight">The target's height in pixels.</param>
    public void Draw(SpriteBatch batch, in RemasteredBinocularView view, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);

        // No full-screen wash and no opaque frame: the lens post-process owns the glass, so the interface stays crisp
        // chrome over it. Thin rails and corner ticks read as an optics frame without tinting the picture a second time.
        float inset = context.Px(30f);
        UiRect frame = new(inset, inset, context.SafeRect.Width - (inset * 2f), context.SafeRect.Height - (inset * 2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, frame.Width, context.Px(2f)), Signal.WithAlpha(190));
        painter.FillRect(new UiRect(frame.X, frame.Bottom - context.Px(2f), frame.Width, context.Px(2f)), Signal.WithAlpha(120));
        DrawCornerTicks(painter, context, frame, Signal.WithAlpha(170));

        if (_theme is { } theme)
        {
            HudCompassStrip.Draw(painter, context, theme, view.HeadingRadians);
        }

        float centreX = context.SafeRect.X + (context.SafeRect.Width * 0.5f);
        float centreY = context.SafeRect.Y + (context.SafeRect.Height * 0.5f);
        DrawReticle(painter, context, centreX, centreY);

        foreach (EnemyOverlayBox target in view.Contacts)
        {
            if (target.TryClipToViewport(screenWidth, screenHeight, out EnemyOverlayBox clipped))
            {
                DrawContactMarker(painter, context, clipped, view.Seconds);
            }
        }

        // The range is the crosshair raycast the caller supplies -- the distance to whatever the centre rests on -- as the
        // vanilla readout shows, not the nearest detected contact. Capped at a thousand metres, as the retail widget is.
        string range = view.RangeMetres is { } metres && float.IsFinite(metres) && metres > 0f && metres < 1000f
            ? FormattableString.Invariant($"RANGE  {metres:0}M")
            : "RANGE  1000+M";
        float magnification = float.IsFinite(view.Magnification) && view.Magnification > 0f ? view.Magnification : 1f;
        UiVectors.DrawTracked(
            painter, body, "OPTICS // BINOCULAR",
            new UiVec2(frame.X + context.Px(18f), frame.Bottom - context.Px(48f)), context.Px(15f), Signal, context.Px(2f));
        painter.DrawText(
            body, FormattableString.Invariant($"x{magnification:0.0}    {range}"),
            new UiVec2(frame.Right - context.Px(250f), frame.Bottom - context.Px(48f)), context.Px(15f), Text);
    }

    /// <summary>The colour a contact's marker is drawn in, by its detector team classification.</summary>
    private static Bgra32 MarkerColour(BinocularMarkerColor marker) => marker switch
    {
        BinocularMarkerColor.Team => Bgra32.FromRgb(120, 235, 140),
        BinocularMarkerColor.Disabled => Bgra32.FromRgb(120, 170, 255),
        _ => Bgra32.FromRgb(255, 72, 72),
    };

    /// <summary>Draws one detected contact: a bobbing chevron over the target and a glowing range label.</summary>
    private void DrawContactMarker(UiPainter painter, LayoutContext context, EnemyOverlayBox target, float seconds)
    {
        float alpha = Math.Clamp(target.Alpha, 0f, 1f);
        if (alpha <= 0f)
        {
            return;
        }

        float centreX = (target.Left + target.Right) * 0.5f;
        Bgra32 colour = MarkerColour(target.Marker);

        // The acquisition flourish reuses the detector's own entry pulse: while PulseScale is still
        // large the contact is freshly seen, so an expanding, fading ring collapses onto the chevron.
        if (target.PulseScale > 1.05f)
        {
            float boxCentreY = (target.Top + target.Bottom) * 0.5f;
            float ringRadius = context.Px(9f) * target.PulseScale * 0.5f;
            float ringFade = Math.Clamp((target.PulseScale - 1f) / 10f, 0f, 1f) * alpha * 0.7f;
            UiVectors.StrokeRing(
                painter, new UiVec2(centreX, boxCentreY), ringRadius,
                MathF.Max(1f, context.Px(2f)), colour.WithAlpha((byte)(255f * ringFade)));
        }

        // The chevron floats just above the contact, bobbing on a per-target phase so a cluster of
        // marks does not pulse in lockstep. A dark backing chevron keeps it legible over bright terrain.
        float bob = context.Px(4f) * MathF.Sin((seconds * 2.2f) + target.TargetId);
        var apex = new UiVec2(centreX, target.Top - context.Px(7f) + bob);
        float halfWidth = context.Px(9f);
        float height = context.Px(11f);
        UiVectors.DrawChevron(
            painter, new UiVec2(apex.X, apex.Y + context.Px(1.5f)),
            halfWidth + context.Px(1.5f), height + context.Px(2f), Bgra32.FromRgba(0, 0, 0, (byte)(130f * alpha)));
        UiVectors.DrawChevron(painter, apex, halfWidth, height, colour.WithAlpha((byte)(255f * alpha)));

        // The range, above the chevron, glowing so it reads as projected onto the optics.
        string label = $"{MathF.Max(0f, target.DistanceMetres):0}M";
        float labelSize = context.Px(15f);
        UiFont labelFont = _display ?? _body!;
        float labelTop = apex.Y - height - labelFont.LineHeight(labelSize) - context.Px(4f);
        DrawGlowLabel(painter, label, centreX, labelTop, labelSize, alpha);
    }

    /// <summary>Draws a centred, glowing label, using the distance-field face when one loaded.</summary>
    private void DrawGlowLabel(UiPainter painter, string text, float centreX, float topY, float size, float alpha)
    {
        Bgra32 white = Bgra32.FromRgb(238, 246, 250).WithAlpha((byte)(255f * alpha));
        if (_display is { } display)
        {
            float width = display.MeasureWidth(text, size);
            SdfTextParams effects = SdfTextParams.Plain(0f)
                .WithOutline(Bgra32.FromRgba(0, 0, 0, (byte)(190f * alpha)), 0.18f)
                .WithGlow(Signal, 0.5f, 0.5f * alpha);
            painter.DrawText(display, text, new UiVec2(centreX - (width * 0.5f), topY), size, white, effects);
        }
        else if (_body is { } body)
        {
            float width = body.MeasureWidth(text, size);
            Bgra32 backing = Bgra32.FromRgba(0, 0, 0, (byte)(170f * alpha));
            painter.DrawText(body, text, new UiVec2(centreX - (width * 0.5f) + 1f, topY + 1f), size, backing);
            painter.DrawText(body, text, new UiVec2(centreX - (width * 0.5f), topY), size, white);
        }
    }

    /// <summary>Draws the gapped rangefinder reticle: crosshair arms, a framing ring and a centre dot.</summary>
    private static void DrawReticle(UiPainter painter, LayoutContext context, float centreX, float centreY)
    {
        Bgra32 mint = Signal.WithAlpha(210);
        float arm = context.Px(24f);
        float gap = context.Px(10f);
        float weight = MathF.Max(1f, context.Px(1.5f));
        painter.FillRect(new UiRect(centreX - gap - arm, centreY - (weight * 0.5f), arm, weight), mint);
        painter.FillRect(new UiRect(centreX + gap, centreY - (weight * 0.5f), arm, weight), mint);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY - gap - arm, weight, arm), mint);
        painter.FillRect(new UiRect(centreX - (weight * 0.5f), centreY + gap, weight, arm), mint);
        UiVectors.StrokeRing(
            painter, new UiVec2(centreX, centreY), context.Px(3.4f), MathF.Max(1f, context.Px(1.2f)), mint.WithAlpha(150));
        UiVectors.FillDisc(painter, new UiVec2(centreX, centreY), MathF.Max(1f, context.Px(1.6f)), Accent);
    }

    /// <summary>Draws short L-brackets at the corners of the optics frame.</summary>
    private static void DrawCornerTicks(UiPainter painter, LayoutContext context, UiRect frame, Bgra32 colour)
    {
        float length = context.Px(28f);
        float weight = MathF.Max(1f, context.Px(2f));
        painter.FillRect(new UiRect(frame.X, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Y, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Y, weight, length), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.X, frame.Bottom - length, weight, length), colour);
        painter.FillRect(new UiRect(frame.Right - length, frame.Bottom - weight, length, weight), colour);
        painter.FillRect(new UiRect(frame.Right - weight, frame.Bottom - length, weight, length), colour);
    }

    private static Bgra32 Text => UiPalette.Text;

    private static Bgra32 Accent => UiPalette.Accent;

    private static Bgra32 Signal => UiPalette.Success;
}
