using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The weapon wheel: the world behind blurred, darkened and pushed in, and over it a ring of glass sectors round the aim, one a
/// weapon, its picture and rounds; the one pointed at lifts out, bright, and the middle names it. In the theme's colours, so
/// Project I.G.I.'s is phosphor and I.G.I. 2's is blue.
/// </summary>
/// <remarks>
/// <para>
/// The ring's first sector is straight up and the rest follow clockwise (<see cref="OpenIGI.Game.Player.WeaponWheel"/>). It opens
/// by scaling up from a little smaller and fading in, with the backdrop's blur, as the wheel's openness eases.
/// </para>
/// <para>
/// Sized by the screen's height rather than the theme's scale, so it is the same size in either game; its other measures are in
/// wheel pixels, a 250th of its outer radius.
/// </para>
/// </remarks>
public static class HudWheel
{
    /// <summary>The ring's outer radius, as a share of the screen's height.</summary>
    public const float OuterShare = 0.235f;

    /// <summary>The ring's inner radius, as a share of its outer.</summary>
    public const float InnerShare = 0.48f;

    /// <summary>The ring's outer radius its other measures are drawn against, in wheel pixels.</summary>
    public const float OuterRadius = 250f;

    /// <summary>How far the sector pointed at lifts out, in wheel pixels.</summary>
    public const float Lift = 12f;

    /// <summary>How hard the world behind is blurred when the wheel is open, in pixels at the full screen.</summary>
    public const float BlurSigma = 22f;

    /// <summary>Draws the wheel, if it is open at all.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    public static void Draw(HudDrawContext context, HudModel model)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        HudWheelView wheel = model.Wheel;
        if (wheel.Openness <= 0f || wheel.Entries.Count == 0)
        {
            return;
        }

        float open = Easing.Evaluate(Ease.OutCubic, wheel.Openness);
        HudPalette palette = context.Palette;
        UiVec2 centre = new(context.Width * 0.5f, context.Height * 0.5f);

        // The world behind: blurred, darkened and pushed in a little, its edges darker still -- a little past the screen's
        // edges, so the visor's bend never pulls an unblurred rim in from them.
        float over = MathF.Max(context.Width, context.Height) * 0.06f;
        UiRect screen = new(-over, -over, context.Width + (over * 2f), context.Height + (over * 2f));
        context.Painter.DrawBackdrop(screen, BlurSigma * open, Bgra32.FromRgba(4, 6, 8, (byte)(105 * open)), 1f + (0.035f * open));
        float corner = MathF.Max(context.Width, context.Height) * 0.75f;
        context.Painter.FillRadialGradient(
            screen,
            centre,
            new UiVec2(corner, corner),
            [new UiGradientStop(0.25f, HudDrawContext.Fade(Bgra32.Black, 0f)), new UiGradientStop(1f, HudDrawContext.Fade(Bgra32.Black, 0.45f * open))]);

        float grow = 0.86f + (0.14f * open);
        float outer = context.Height * OuterShare * grow, inner = outer * InnerShare;
        float unit = outer / OuterRadius;
        int count = wheel.Entries.Count;
        float step = MathF.Tau / count;
        for (int i = 0; i < count; i++)
        {
            bool hovered = i == wheel.Hovered;
            float middle = i * step;
            UiVec2 at = Point(centre, hovered ? Lift * unit * open : 0f, middle);
            Sector(context, at, inner, outer, middle - (step * 0.5f), middle + (step * 0.5f), unit, hovered, i == wheel.Current, open);
            Entry(context, wheel.Entries[i], Point(at, (inner + outer) * 0.5f, middle), outer - inner, step * ((inner + outer) * 0.5f), unit, hovered, open);
        }

        Middle(context, wheel, centre, inner - (10f * unit), unit, open);
        Pointer(context, wheel, centre, inner - (4f * unit), unit, open);

        // How to use it, under the ring.
        const string Hint = "RELEASE TO EQUIP";
        float hintSize = 13f * unit;
        float hintWidth = UiPainter.MeasureLabel(context.Fonts.Label, Hint, hintSize, 0.18f);
        context.Painter.DrawLabel(context.Fonts.Label, Hint, new UiVec2(centre.X - (hintWidth * 0.5f), centre.Y + outer + (22f * unit)), hintSize, HudDrawContext.Fade(palette.Dim, open), 0.18f);
    }

    // One sector: glass between two arcs, cut back from its neighbours by a gap and filled strip by strip, so a sector wider than
    // half a turn fills as truly as a narrow one; the one pointed at bright, with a thick rim along its outer edge; the one in
    // hand marked with a notch on its inner edge.
    private static void Sector(HudDrawContext context, UiVec2 centre, float inner, float outer, float from, float to, float unit, bool hovered, bool current, float open)
    {
        HudPalette palette = context.Palette;
        int steps = Math.Max(6, (int)MathF.Ceiling((to - from) / 0.08f));
        Span<UiVec2> outerEdge = stackalloc UiVec2[steps + 1];
        Span<UiVec2> innerEdge = stackalloc UiVec2[steps + 1];
        float gap = 5f * unit;
        float outerTrim = gap / outer, innerTrim = gap / inner;
        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            outerEdge[s] = Point(centre, outer, from + outerTrim + ((to - from - (2f * outerTrim)) * t));
            innerEdge[s] = Point(centre, inner, from + innerTrim + ((to - from - (2f * innerTrim)) * t));
        }

        Bgra32 glass = HudDrawContext.Fade(palette.Glass, open);
        Bgra32 lit = HudDrawContext.Fade(palette.Primary, 0.32f * open);
        Bgra32 rimColour = HudDrawContext.Fade(palette.Highlight, open);
        float rim = MathF.Max(2f, 4f * unit);
        for (int s = 0; s < steps; s++)
        {
            context.Painter.FillQuad(innerEdge[s], outerEdge[s], outerEdge[s + 1], innerEdge[s + 1], glass);
            if (hovered)
            {
                context.Painter.FillQuad(innerEdge[s], outerEdge[s], outerEdge[s + 1], innerEdge[s + 1], lit);
                UiVec2 a = Point(centre, outer - rim, Angle(centre, outerEdge[s])), b = Point(centre, outer - rim, Angle(centre, outerEdge[s + 1]));
                context.Painter.FillQuad(outerEdge[s], outerEdge[s + 1], b, a, rimColour);
            }
        }

        // Its outline: both arcs and both cut sides.
        float hairline = MathF.Max(1f, 1.2f * unit);
        Bgra32 line = HudDrawContext.Fade(hovered ? palette.Highlight : palette.Primary, (hovered ? 0.9f : 0.35f) * open);
        for (int s = 0; s < steps; s++)
        {
            context.Painter.StrokeLine(outerEdge[s], outerEdge[s + 1], hairline, line);
            context.Painter.StrokeLine(innerEdge[s], innerEdge[s + 1], hairline, line);
        }

        context.Painter.StrokeLine(innerEdge[0], outerEdge[0], hairline, line);
        context.Painter.StrokeLine(innerEdge[steps], outerEdge[steps], hairline, line);

        if (current)
        {
            float middle = (from + to) * 0.5f;
            float size = 6f * unit;
            UiVec2 tip = Point(centre, inner + size + (3f * unit), middle);
            UiVec2 left = Point(centre, inner + (3f * unit), middle - (size / inner));
            UiVec2 right = Point(centre, inner + (3f * unit), middle + (size / inner));
            context.Painter.FillTriangle(left, tip, right, HudDrawContext.Fade(palette.Accent, open));
        }
    }

    // A weapon's picture, fitted to its sector, and its rounds under it; its name where it has no picture.
    private static void Entry(HudDrawContext context, HudWheelEntry entry, UiVec2 at, float depth, float width, float unit, bool hovered, float open)
    {
        HudPalette palette = context.Palette;
        Bgra32 colour = HudDrawContext.Fade(hovered ? palette.Highlight : palette.Primary, (hovered ? 1f : 0.75f) * open);
        float countSize = (hovered ? 15f : 13f) * unit;
        float boxWidth = Math.Min(width * 0.62f, 96f * unit), boxHeight = depth * 0.42f;
        float w = 0f, h = 0f;
        if (entry.Icon == HudIcon.None || !context.Icons.TryMeasure(entry.Icon, out w, out h) || w <= 0f || h <= 0f)
        {
            float nameSize = 14f * unit;
            float nameWidth = UiPainter.MeasureLabel(context.Fonts.Name, entry.Name, nameSize, 0.04f);
            context.Painter.DrawLabel(context.Fonts.Name, entry.Name, new UiVec2(at.X - (nameWidth * 0.5f), at.Y - nameSize), nameSize, colour, 0.04f);
        }
        else
        {
            float scale = MathF.Min(boxWidth / w, boxHeight / h) * (hovered ? 1.08f : 1f);
            context.DrawIcon(entry.Icon, at.X - (w * scale * 0.5f), at.Y - (h * scale * 0.5f) - (6f * unit), scale, colour);
        }

        if (entry.Count.Length > 0)
        {
            float countWidth = UiPainter.MeasureLabel(context.Fonts.Value, entry.Count, countSize, 0.06f);
            context.Painter.DrawLabel(context.Fonts.Value, entry.Count, new UiVec2(at.X - (countWidth * 0.5f), at.Y + (boxHeight * 0.5f) - (2f * unit)), countSize, colour, 0.06f);
        }
    }

    // The middle: a dark glass disc naming the weapon pointed at, its rounds under its name.
    private static void Middle(HudDrawContext context, HudWheelView wheel, UiVec2 centre, float radius, float unit, float open)
    {
        HudPalette palette = context.Palette;
        Span<UiVec2> disc = stackalloc UiVec2[40];
        for (int i = 0; i < disc.Length; i++)
        {
            disc[i] = Point(centre, radius, i * MathF.Tau / disc.Length);
        }

        context.Painter.FillPolygon(disc, HudDrawContext.Fade(palette.Glass, open));
        context.Painter.StrokePolygon(disc, MathF.Max(1f, 1.2f * unit), HudDrawContext.Fade(palette.Primary, 0.4f * open));

        const string Caption = "WEAPONS";
        float captionSize = 11f * unit;
        float captionWidth = UiPainter.MeasureLabel(context.Fonts.Label, Caption, captionSize, 0.3f);
        context.Painter.DrawLabel(context.Fonts.Label, Caption, new UiVec2(centre.X - (captionWidth * 0.5f), centre.Y - (radius * 0.62f)), captionSize, HudDrawContext.Fade(palette.Dim, open), 0.3f);

        if (wheel.Hovered < 0 || wheel.Hovered >= wheel.Entries.Count)
        {
            return;
        }

        HudWheelEntry entry = wheel.Entries[wheel.Hovered];
        string name = entry.Name.ToUpper(CultureInfo.InvariantCulture);
        float nameSize = 20f * unit;
        float nameWidth = UiPainter.MeasureLabel(context.Fonts.Name, name, nameSize, 0.05f);
        if (nameWidth > radius * 1.7f)
        {
            nameSize *= radius * 1.7f / nameWidth;
            nameWidth = radius * 1.7f;
        }

        context.Painter.DrawLabel(context.Fonts.Name, name, new UiVec2(centre.X - (nameWidth * 0.5f), centre.Y - (nameSize * 0.8f)), nameSize, HudDrawContext.Fade(palette.Highlight, open), 0.05f);
        if (entry.Count.Length > 0)
        {
            float countSize = 26f * unit;
            float countWidth = UiPainter.MeasureLabel(context.Fonts.Value, entry.Count, countSize, 0.04f);
            context.Painter.DrawLabel(context.Fonts.Value, entry.Count, new UiVec2(centre.X - (countWidth * 0.5f), centre.Y + (6f * unit)), countSize, HudDrawContext.Fade(palette.Primary, open), 0.04f);
        }
    }

    // Where the mouse points: a short bright arc on the inner edge, with a point out towards the sector.
    private static void Pointer(HudDrawContext context, HudWheelView wheel, UiVec2 centre, float radius, float unit, float open)
    {
        if (wheel.PointerAngle is not { } angle)
        {
            return;
        }

        Bgra32 colour = HudDrawContext.Fade(context.Palette.Highlight, open);
        float half = 0.16f, thickness = MathF.Max(2f, 3f * unit);
        const int Pieces = 8;
        for (int i = 0; i < Pieces; i++)
        {
            float a0 = angle - half + (2f * half * i / Pieces), a1 = angle - half + (2f * half * (i + 1) / Pieces);
            context.Painter.FillQuad(
                Point(centre, radius - thickness, a0), Point(centre, radius, a0), Point(centre, radius, a1), Point(centre, radius - thickness, a1), colour);
        }

        float size = 7f * unit;
        context.Painter.FillTriangle(
            Point(centre, radius, angle - (size / radius)), Point(centre, radius + size, angle), Point(centre, radius, angle + (size / radius)), colour);
    }

    // A point at a radius and a way clockwise from straight up.
    private static UiVec2 Point(UiVec2 centre, float radius, float angle) =>
        new(centre.X + (MathF.Sin(angle) * radius), centre.Y - (MathF.Cos(angle) * radius));

    // The way a point lies from the centre, clockwise from straight up.
    private static float Angle(UiVec2 centre, UiVec2 point) => MathF.Atan2(point.X - centre.X, centre.Y - point.Y);
}
