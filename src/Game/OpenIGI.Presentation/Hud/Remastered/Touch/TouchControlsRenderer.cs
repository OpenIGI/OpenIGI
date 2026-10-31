using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Presentation.Hud.Remastered.Touch;

/// <summary>Draws a touch screen's controls: smoked glass, white line pictures, and the title's accent only where something is live.</summary>
/// <remarks>
/// Kept quiet on purpose, the way shooters on a phone draw theirs: every button the same neutral glass with a thin light rim, so
/// the picture shows through and the eye goes to the fight. Colour is spent on two things only -- a button held, and the use
/// button when there is something to use. The weapon's card and the use button show the title's own sprites (the weapon in hand,
/// the action on offer) from its display's model; everything else is a vector picture.
/// </remarks>
public static class TouchControlsRenderer
{
    private static readonly Bgra32 Glass = Bgra32.FromRgba(16, 18, 20, 105);
    private static readonly Bgra32 GlassHeld = Bgra32.FromRgba(255, 255, 255, 70);
    private static readonly Bgra32 Rim = Bgra32.FromRgba(255, 255, 255, 60);
    private static readonly Bgra32 Ink = Bgra32.FromRgba(255, 255, 255, 225);
    private static readonly Bgra32 InkFaint = Bgra32.FromRgba(255, 255, 255, 120);
    private static readonly Bgra32 Shade = Bgra32.FromRgba(0, 0, 0, 90);
    private static readonly Bgra32 DefaultAccent = Bgra32.FromRgba(255, 182, 64, 255);

    /// <summary>Draws the controls in play.</summary>
    /// <param name="batch">The batch to draw into, begun.</param>
    /// <param name="context">The screen's layout.</param>
    /// <param name="controls">The controls, read this frame.</param>
    /// <param name="theme">The title's theme, for its accent, or null.</param>
    /// <param name="icons">The title's pictures, or null for vector pictures alone.</param>
    /// <param name="model">The title's display model, for the weapon in hand and the action on offer, or null.</param>
    /// <param name="font">The face the weapon's rounds are written in, or null to leave them out.</param>
    public static void Draw(
        SpriteBatch batch, LayoutContext context, TouchControls controls, HudTheme? theme, IHudIconSource? icons, HudModel? model, UiFont? font)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(controls);

        UiPainter painter = UiPainter.Create(batch, context);
        Bgra32 accent = theme?.Palette.Accent ?? DefaultAccent;
        float rim = MathF.Max(1f, context.Px(2f));
        float stroke = MathF.Max(1.5f, context.Px(4f));

        if (controls.Mode == TouchControlMode.Play)
        {
            DrawStick(painter, controls, rim, stroke);
        }

        bool offered = model is { Actions.Count: > 0 };
        foreach (TouchButtonSpot spot in controls.Layout)
        {
            bool held = controls.Held.Contains(spot.Button);
            if (spot.Card)
            {
                DrawWeaponCard(painter, batch, context, spot, held, icons, model, font, rim, stroke);
                continue;
            }

            bool live = spot.Button == TouchButton.Use && offered;
            Disc(painter, spot.Centre, spot.Radius, held ? GlassHeld : Glass, held || live ? accent : Rim, held || live ? rim * 1.6f : rim);

            HudIcon action = spot.Button == TouchButton.Use && model is { Actions: [var first, ..] } ? first.Icon : HudIcon.None;
            if (action.IsSome && icons is not null
                && DrawPicture(batch, icons, action, spot.Centre, spot.Radius * 1.25f, spot.Radius * 1.05f, Ink))
            {
                continue;
            }

            float size = spot.Radius * (spot.Button == TouchButton.Fire ? 0.5f : 0.46f);
            Bgra32 ink = spot.Button == TouchButton.Use && !offered && !held ? InkFaint : Ink;
            DrawGlyph(painter, spot.Button, spot.Centre, size, spot.Radius > context.Px(80f) ? stroke * 1.3f : stroke, ink);
        }
    }

    private static void Disc(UiPainter painter, UiVec2 centre, float radius, Bgra32 fill, Bgra32 edge, float thickness)
    {
        painter.FillArc(centre, radius, radius + MathF.Max(2f, radius * 0.06f), 0f, MathF.Tau, Shade);
        painter.FillArc(centre, 0f, radius, 0f, MathF.Tau, fill);
        painter.FillArc(centre, radius - thickness, radius, 0f, MathF.Tau, edge);
    }

    private static void DrawStick(UiPainter painter, TouchControls controls, float rim, float stroke)
    {
        float reach = controls.StickReachPixels;
        UiVec2 origin = controls.StickOrigin ?? controls.StickRest;
        bool active = controls.StickOrigin is not null;
        painter.PushOpacity(active ? 1f : 0.6f);

        painter.FillArc(origin, 0f, reach, 0f, MathF.Tau, Bgra32.FromRgba(16, 18, 20, 60));
        painter.FillArc(origin, reach - rim, reach, 0f, MathF.Tau, Rim);

        // A caret inside the rim at each of the four directions it walks.
        float caret = reach * 0.09f;
        float at = reach - (caret * 1.8f);
        painter.DrawChevron(new UiVec2(origin.X, origin.Y - at), caret, UiDirection.Up, stroke * 0.6f, InkFaint);
        painter.DrawChevron(new UiVec2(origin.X, origin.Y + at), caret, UiDirection.Down, stroke * 0.6f, InkFaint);
        painter.DrawChevron(new UiVec2(origin.X - at, origin.Y), caret, UiDirection.Left, stroke * 0.6f, InkFaint);
        painter.DrawChevron(new UiVec2(origin.X + at, origin.Y), caret, UiDirection.Right, stroke * 0.6f, InkFaint);

        UiVec2 knob = new(origin.X + (controls.Stick.X * reach), origin.Y + (controls.Stick.Y * reach));
        float knobRadius = reach * 0.4f;
        Disc(painter, knob, knobRadius, active ? GlassHeld : Glass, active ? Ink : Rim, rim);
        painter.PopOpacity();
    }

    private static void DrawWeaponCard(
        UiPainter painter, SpriteBatch batch, LayoutContext context, TouchButtonSpot spot, bool held,
        IHudIconSource? icons, HudModel? model, UiFont? font, float rim, float stroke)
    {
        UiRect box = spot.Box;
        float cut = box.Height * 0.22f;
        painter.FillCutRect(box with { Y = box.Y + context.Px(3f) }, cut, UiCorners.TopLeft | UiCorners.BottomRight, Shade);
        painter.FillCutRect(box, cut, UiCorners.TopLeft | UiCorners.BottomRight, held ? GlassHeld : Glass);
        painter.StrokeCutRect(box, cut, UiCorners.TopLeft | UiCorners.BottomRight, rim, Rim);

        // The swap mark in the corner says what a tap does.
        float mark = box.Height * 0.16f;
        UiVec2 corner = new(box.X + (mark * 1.6f), box.Y + (mark * 1.5f));
        DrawGlyph(painter, TouchButton.Weapon, corner, mark, stroke * 0.7f, InkFaint);

        HudWeaponView? weapon = model?.Weapon;
        string? rounds = weapon is { Magazine: { } loaded }
            ? weapon.Value.Reserve is { } spare ? $"{loaded.ToString(CultureInfo.InvariantCulture)} / {spare.ToString(CultureInfo.InvariantCulture)}" : loaded.ToString(CultureInfo.InvariantCulture)
            : null;
        float textWidth = 0f;
        float textSize = box.Height * 0.3f;
        if (rounds is not null && font is not null)
        {
            UiVec2 extent = font.Measure(rounds, textSize);
            textWidth = extent.X + (box.Height * 0.25f);
            painter.DrawText(font, rounds, new UiVec2(box.Right - textWidth, box.Bottom - extent.Y - (box.Height * 0.16f)), textSize, Ink);
        }

        UiVec2 middle = new(box.X + ((box.Width - textWidth) * 0.5f) + (mark * 0.8f), box.Y + (box.Height * 0.5f));
        if (weapon is { Icon.IsSome: true } shown && icons is not null
            && DrawPicture(batch, icons, shown.Icon, middle, (box.Width - textWidth) * 0.7f, box.Height * 0.66f, Ink))
        {
            return;
        }

        DrawGlyph(painter, TouchButton.Weapon, middle, box.Height * 0.25f, stroke, Ink);
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
        void Dot(float x, float y, float r) => painter.FillDot(P(x, y), r * size, ink);

        switch (button)
        {
            case TouchButton.Fire:
                // A round, laid at the slant of a thumb's press.
                ReadOnlySpan<UiVec2> round =
                [
                    P(0.85f, -0.85f), P(0.55f, -0.35f), P(-0.45f, 0.65f), P(-0.65f, 0.45f), P(0.35f, -0.55f),
                ];
                painter.FillPolygon(round, ink);
                Line(-0.55f, 0.75f, -0.85f, 1.05f);
                Line(-0.75f, 0.55f, -1.05f, 0.85f);
                break;
            case TouchButton.Scope:
                Ring(0f, 0f, 0.72f);
                Line(0f, -1.05f, 0f, -0.35f);
                Line(0f, 0.35f, 0f, 1.05f);
                Line(-1.05f, 0f, -0.35f, 0f);
                Line(0.35f, 0f, 1.05f, 0f);
                Dot(0f, 0f, 0.1f);
                break;
            case TouchButton.Reload:
                // A magazine, and the turn of its going in.
                painter.FillQuad(P(-0.35f, -0.75f), P(0.15f, -0.75f), P(0.35f, 0.75f), P(-0.15f, 0.75f), ink);
                painter.FillArc(c, size * 0.86f, (size * 0.86f) + stroke, 0.45f, 2.6f, ink);
                painter.FillTriangle(P(-0.15f, 0.62f), P(0.32f, 0.98f), P(-0.25f, 1.12f), ink);
                break;
            case TouchButton.Jump:
                painter.DrawChevron(P(0f, -0.3f), size * 0.62f, UiDirection.Up, stroke, ink);
                painter.DrawChevron(P(0f, 0.32f), size * 0.62f, UiDirection.Up, stroke, ink);
                break;
            case TouchButton.Crouch:
                // A figure down on one knee.
                Dot(0.15f, -0.72f, 0.2f);
                Line(0.08f, -0.45f, -0.12f, 0.15f);
                Line(-0.12f, 0.15f, 0.45f, 0.2f);
                Line(0.45f, 0.2f, 0.45f, 0.8f);
                Line(-0.12f, 0.15f, -0.45f, 0.8f);
                Line(0.05f, -0.3f, 0.55f, -0.3f);
                break;
            case TouchButton.Peek:
                // A head round a wall's edge.
                painter.FillRect(new UiRect(c.X - (size * 0.85f), c.Y - (size * 0.95f), size * 0.35f, size * 1.9f), ink);
                Dot(0.1f, -0.45f, 0.22f);
                Line(-0.05f, -0.2f, 0.25f, 0.85f);
                painter.DrawChevron(P(0.65f, -0.45f), size * 0.22f, UiDirection.Right, stroke * 0.8f, ink);
                break;
            case TouchButton.Use:
                // An open hand's reach: a finger to a switch.
                Ring(0f, 0f, 0.78f);
                Dot(0f, 0f, 0.2f);
                break;
            case TouchButton.Weapon:
                Line(-0.8f, -0.32f, 0.55f, -0.32f);
                painter.FillTriangle(P(0.95f, -0.32f), P(0.5f, -0.68f), P(0.5f, 0.04f), ink);
                Line(0.8f, 0.38f, -0.55f, 0.38f);
                painter.FillTriangle(P(-0.95f, 0.38f), P(-0.5f, 0.02f), P(-0.5f, 0.74f), ink);
                break;
            case TouchButton.Pause:
                painter.FillRect(new UiRect(c.X - (size * 0.48f), c.Y - (size * 0.6f), size * 0.3f, size * 1.2f), ink);
                painter.FillRect(new UiRect(c.X + (size * 0.18f), c.Y - (size * 0.6f), size * 0.3f, size * 1.2f), ink);
                break;
            case TouchButton.Map:
                ReadOnlySpan<UiVec2> fold =
                [
                    P(-0.9f, -0.62f), P(-0.3f, -0.82f), P(0.3f, -0.62f), P(0.9f, -0.82f),
                    P(0.9f, 0.62f), P(0.3f, 0.82f), P(-0.3f, 0.62f), P(-0.9f, 0.82f),
                ];
                painter.StrokePolygon(fold, stroke, ink);
                Line(-0.3f, -0.82f, -0.3f, 0.62f);
                Line(0.3f, -0.62f, 0.3f, 0.82f);
                break;
            case TouchButton.Talk:
                KitIcons.Draw(painter, KitIcon.Microphone, c, size * 1.5f, ink);
                break;
            case TouchButton.Binoculars:
                Ring(-0.46f, 0.2f, 0.38f);
                Ring(0.46f, 0.2f, 0.38f);
                Line(-0.12f, -0.15f, 0.12f, -0.15f);
                Line(-0.62f, -0.15f, -0.4f, -0.68f);
                Line(0.62f, -0.15f, 0.4f, -0.68f);
                break;
            case TouchButton.ZoomIn:
                Line(-0.6f, 0f, 0.6f, 0f);
                Line(0f, -0.6f, 0f, 0.6f);
                break;
            case TouchButton.ZoomOut:
                Line(-0.6f, 0f, 0.6f, 0f);
                break;
            case TouchButton.Close:
                Line(-0.6f, -0.6f, 0.6f, 0.6f);
                Line(-0.6f, 0.6f, 0.6f, -0.6f);
                break;
        }
    }
}
