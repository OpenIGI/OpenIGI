using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>Draws a touch screen's controls in the title's remastered look: its glass, its colours, its cut corners and its pictures.</summary>
/// <remarks>
/// The weapon button shows the weapon in hand and the use button the action on offer, each the title's own sprite from its display's
/// model; everything else is drawn as a vector glyph in the theme's ink.
/// </remarks>
public static class TouchControlsRenderer
{
    /// <summary>Draws the controls in play.</summary>
    /// <param name="batch">The batch to draw into, begun.</param>
    /// <param name="context">The screen's layout.</param>
    /// <param name="controls">The controls, read this frame.</param>
    /// <param name="theme">The title's theme, or null for a plain one.</param>
    /// <param name="icons">The title's pictures, or null for glyphs alone.</param>
    /// <param name="model">The title's display model, for the weapon in hand and the action on offer, or null.</param>
    public static void Draw(SpriteBatch batch, LayoutContext context, TouchControls controls, HudTheme? theme, IHudIconSource? icons, HudModel? model)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(controls);

        UiPainter painter = UiPainter.Create(batch, context);
        HudPalette palette = theme?.Palette ?? Plain;
        float hairline = MathF.Max(1f, context.Px((theme?.Motifs.Hairline ?? 1f) * 2f));
        float cornerSize = theme?.Motifs.CornerSize ?? 8f;

        DrawStick(painter, controls, palette, hairline);

        bool actionOffered = model is { Actions.Count: > 0 };
        foreach (TouchButtonSpot spot in controls.Layout)
        {
            bool held = controls.Held.Contains(spot.Button);
            Bgra32 ink = held ? palette.Highlight : palette.Primary;
            Bgra32 rim = held ? palette.Highlight : spot.Button == TouchButton.Use && actionOffered ? palette.Accent : palette.Dim;
            Bgra32 glass = held ? Mix(palette.Glass, palette.Primary, 0.35f) : Deepen(palette.Glass);

            if (spot.Chip)
            {
                float cut = context.Px(cornerSize);
                painter.FillCutRect(spot.Box, cut, UiCorners.TopLeft | UiCorners.BottomRight, glass);
                painter.StrokeCutRect(spot.Box, cut, UiCorners.TopLeft | UiCorners.BottomRight, hairline, rim);
            }
            else
            {
                painter.FillArc(spot.Centre, 0f, spot.Radius, 0f, MathF.Tau, glass);
                painter.FillArc(spot.Centre, spot.Radius - hairline, spot.Radius, 0f, MathF.Tau, rim);
            }

            float size = spot.Chip ? spot.Radius * 0.62f : spot.Radius * 0.48f;
            HudIcon picture = spot.Button switch
            {
                TouchButton.Weapon when model?.Weapon is { Icon.IsSome: true } weapon => weapon.Icon,
                TouchButton.Use when model is { Actions: [var action, ..] } && action.Icon.IsSome => action.Icon,
                _ => HudIcon.None,
            };

            if (picture.IsSome && icons is not null && DrawPicture(batch, icons, picture, spot.Centre, spot.Radius * 1.45f, spot.Radius * 0.95f, ink))
            {
                continue;
            }

            DrawGlyph(painter, spot.Button, spot.Centre, size, hairline * 1.6f, ink);
        }
    }

    private static void DrawStick(UiPainter painter, TouchControls controls, HudPalette palette, float hairline)
    {
        float reach = controls.StickReachPixels;
        UiVec2 origin = controls.StickOrigin ?? controls.StickRest;
        bool active = controls.StickOrigin is not null;
        painter.PushOpacity(active ? 1f : 0.55f);
        painter.FillArc(origin, 0f, reach, 0f, MathF.Tau, Deepen(palette.Glass));
        painter.FillArc(origin, reach - hairline, reach, 0f, MathF.Tau, palette.Dim);

        // Four ticks round the rim, the directions it holds.
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI * 0.5f;
            painter.FillArc(origin, reach - (hairline * 5f), reach - (hairline * 2f), a - 0.06f, a + 0.06f, palette.Primary);
        }

        UiVec2 knob = new(origin.X + (controls.Stick.X * reach), origin.Y + (controls.Stick.Y * reach));
        painter.FillArc(knob, 0f, reach * 0.42f, 0f, MathF.Tau, active ? palette.Primary.WithAlpha(150) : palette.Dim);
        painter.FillArc(knob, reach * 0.42f - hairline, reach * 0.42f, 0f, MathF.Tau, palette.Highlight.WithAlpha(active ? (byte)230 : (byte)120));
        painter.PopOpacity();
    }

    private static bool DrawPicture(SpriteBatch batch, IHudIconSource icons, HudIcon icon, UiVec2 centre, float maxWidth, float maxHeight, Bgra32 ink)
    {
        if (!icons.TryMeasure(icon, out float width, out float height) || width <= 0f || height <= 0f)
        {
            return false;
        }

        float scale = MathF.Min(maxWidth / width, maxHeight / height);
        icons.Draw(batch, icon, centre.X - (width * scale * 0.5f), centre.Y - (height * scale * 0.5f), scale, ink);
        return true;
    }

    /// <summary>The button's picture as strokes: <paramref name="size"/> is half its height.</summary>
    private static void DrawGlyph(UiPainter painter, TouchButton button, UiVec2 c, float size, float stroke, Bgra32 ink)
    {
        UiVec2 P(float x, float y) => new(c.X + (x * size), c.Y + (y * size));
        void Line(float x0, float y0, float x1, float y1) => painter.StrokeLine(P(x0, y0), P(x1, y1), stroke, ink);
        void Ring(float x, float y, float r) => painter.FillArc(P(x, y), (r * size) - (stroke * 0.5f), (r * size) + (stroke * 0.5f), 0f, MathF.Tau, ink);

        switch (button)
        {
            case TouchButton.Fire:
                Ring(0f, 0f, 0.62f);
                Line(0f, -1.05f, 0f, -0.3f);
                Line(0f, 0.3f, 0f, 1.05f);
                Line(-1.05f, 0f, -0.3f, 0f);
                Line(0.3f, 0f, 1.05f, 0f);
                painter.FillDot(c, stroke * 0.9f, ink);
                break;
            case TouchButton.Scope:
                Ring(0f, 0f, 0.85f);
                Ring(0f, 0f, 0.35f);
                Line(0f, -0.85f, 0f, 0.85f);
                Line(-0.85f, 0f, 0.85f, 0f);
                break;
            case TouchButton.Reload:
                painter.FillArc(c, (0.7f * size) - (stroke * 0.5f), (0.7f * size) + (stroke * 0.5f), 0.55f, MathF.Tau - 0.35f, ink);
                painter.FillTriangle(P(0.05f, -0.98f), P(0.05f, -0.42f), P(0.5f, -0.7f), ink);
                break;
            case TouchButton.Jump:
                painter.DrawChevron(P(0f, -0.25f), size * 0.7f, UiDirection.Up, stroke, ink);
                painter.DrawChevron(P(0f, 0.35f), size * 0.7f, UiDirection.Up, stroke, ink);
                break;
            case TouchButton.Crouch:
                painter.DrawChevron(P(0f, -0.2f), size * 0.7f, UiDirection.Down, stroke, ink);
                Line(-0.75f, 0.6f, 0.75f, 0.6f);
                break;
            case TouchButton.Use:
                // A hand's reach to a handle: a door's lever.
                Ring(0f, 0f, 0.8f);
                painter.FillRect(new UiRect(c.X - (size * 0.45f), c.Y - (stroke * 0.8f), size * 0.9f, stroke * 1.6f), ink);
                painter.FillDot(P(-0.45f, 0f), stroke * 1.4f, ink);
                break;
            case TouchButton.Weapon:
                Line(-0.85f, -0.35f, 0.6f, -0.35f);
                painter.FillTriangle(P(0.95f, -0.35f), P(0.5f, -0.7f), P(0.5f, 0f), ink);
                Line(0.85f, 0.4f, -0.6f, 0.4f);
                painter.FillTriangle(P(-0.95f, 0.4f), P(-0.5f, 0.05f), P(-0.5f, 0.75f), ink);
                break;
            case TouchButton.Peek:
                // A figure leaning out from behind a wall.
                Line(-0.7f, -0.95f, -0.7f, 0.95f);
                Ring(0.25f, -0.5f, 0.24f);
                Line(0.08f, -0.22f, -0.3f, 0.85f);
                break;
            case TouchButton.Pause:
                painter.FillRect(new UiRect(c.X - (size * 0.5f), c.Y - (size * 0.6f), size * 0.32f, size * 1.2f), ink);
                painter.FillRect(new UiRect(c.X + (size * 0.18f), c.Y - (size * 0.6f), size * 0.32f, size * 1.2f), ink);
                break;
            case TouchButton.Map:
                ReadOnlySpan<UiVec2> fold =
                [
                    P(-0.95f, -0.6f), P(-0.32f, -0.8f), P(0.32f, -0.6f), P(0.95f, -0.8f),
                    P(0.95f, 0.6f), P(0.32f, 0.8f), P(-0.32f, 0.6f), P(-0.95f, 0.8f),
                ];
                painter.StrokePolygon(fold, stroke, ink);
                Line(-0.32f, -0.8f, -0.32f, 0.6f);
                Line(0.32f, -0.6f, 0.32f, 0.8f);
                break;
            case TouchButton.Binoculars:
                Ring(-0.48f, 0.15f, 0.42f);
                Ring(0.48f, 0.15f, 0.42f);
                Line(-0.2f, -0.45f, 0.2f, -0.45f);
                Line(-0.48f, -0.27f, -0.32f, -0.7f);
                Line(0.48f, -0.27f, 0.32f, -0.7f);
                break;
            case TouchButton.ZoomIn:
                Line(-0.7f, 0f, 0.7f, 0f);
                Line(0f, -0.7f, 0f, 0.7f);
                break;
            case TouchButton.ZoomOut:
                Line(-0.7f, 0f, 0.7f, 0f);
                break;
        }
    }

    /// <summary>Where a title lends no theme: white ink on smoked glass.</summary>
    private static readonly HudPalette Plain = new(
        Primary: Bgra32.FromRgba(236, 235, 230, 235),
        Dim: Bgra32.FromRgba(236, 235, 230, 110),
        Highlight: Bgra32.FromRgb(255, 255, 255),
        Accent: Bgra32.FromRgba(255, 182, 64, 255),
        Danger: Bgra32.FromRgba(255, 62, 48, 255),
        Glass: Bgra32.FromRgba(0, 0, 0, 60),
        Health: static _ => Bgra32.FromRgb(236, 235, 230),
        Visibility: static _ => Bgra32.FromRgb(236, 235, 230));

    /// <summary>The theme's glass, made heavy enough to read against a bright sky.</summary>
    private static Bgra32 Deepen(Bgra32 glass) => glass.WithAlpha((byte)Math.Min(255, glass.A + 70));

    private static Bgra32 Mix(Bgra32 a, Bgra32 b, float t) => Bgra32.FromRgba(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)),
        (byte)Math.Max(a.A, (int)(b.A * t)));
}
