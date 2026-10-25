using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The weapon wheel, in the theme's colours: a ring of glass sectors round the aim, one a weapon, lit at the rim; the one
/// pointed at lifts out, brightens and throws a glow past the ring; a dial of ticks runs round the outside, brightest where the
/// mouse points, with a few arcs turning slowly beyond it; the middle shows the weapon pointed at, large, with its name and rounds.
/// </summary>
/// <remarks>
/// <para>
/// The world behind is the visor pass's to take out of time (drained, smeared and grained: <c>Ui/HudFx.slang</c>); where that
/// pass is not running, the wheel lays its own blurred, darkened backdrop.
/// </para>
/// <para>
/// It opens turning in from a little behind, a little smaller, its sectors coming in one after another round the ring. The ring's
/// first sector is straight up and the rest follow clockwise (<see cref="OpenIGI.Game.Player.WeaponWheel"/>). Sized by the
/// screen's height rather than the theme's scale, so it is the same size in either game; its other measures are in wheel
/// pixels, a 250th of its outer radius.
/// </para>
/// </remarks>
public static class HudWheel
{
    /// <summary>The ring's outer radius, as a share of the screen's height.</summary>
    public const float OuterShare = 0.235f;

    /// <summary>The ring's inner radius, as a share of its outer.</summary>
    public const float InnerShare = 0.5f;

    /// <summary>The ring's outer radius its other measures are drawn against, in wheel pixels.</summary>
    public const float OuterRadius = 250f;

    /// <summary>How far the sector pointed at lifts out, in wheel pixels.</summary>
    public const float Lift = 12f;

    /// <summary>How hard the world behind is blurred when the wheel lays its own backdrop, in pixels at the full screen.</summary>
    public const float BlurSigma = 22f;

    /// <summary>How many ticks the dial round the outside has.</summary>
    public const int DialTicks = 120;

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
        UiVec2 centre = new(context.Width * 0.5f, context.Height * 0.5f);
        if (!context.ShaderBackdrop)
        {
            Backdrop(context, centre, open);
        }

        float grow = 0.82f + (0.18f * open);
        float outer = context.Height * OuterShare * grow, inner = outer * InnerShare;
        float unit = outer / OuterRadius;
        float spin = (1f - open) * -0.45f;
        HudPalette palette = context.Palette;

        // A soft shadow under the whole, so the ring stands off whatever is behind it.
        context.Painter.FillArc(centre, 0f, outer * 1.45f, 0f, MathF.Tau, HudDrawContext.Fade(Bgra32.Black, 0.55f * open), HudDrawContext.Fade(Bgra32.Black, 0f));

        Dial(context, wheel, centre, outer + (12f * unit), unit, spin, open);

        int count = wheel.Entries.Count;
        float step = MathF.Tau / count;
        for (int i = 0; i < count; i++)
        {
            if (i != wheel.Hovered)
            {
                DrawSector(context, wheel, i, centre, inner, outer, step, unit, spin, open);
            }
        }

        // The one pointed at last, so its glow lies over its neighbours.
        if (wheel.Hovered >= 0 && wheel.Hovered < count)
        {
            DrawSector(context, wheel, wheel.Hovered, centre, inner, outer, step, unit, spin, open);
        }

        Middle(context, wheel, centre, inner - (12f * unit), unit, open);
        Pointer(context, wheel, centre, inner - (6f * unit), unit, open);

        // How to use it, under the ring, between two short rules.
        const string Hint = "RELEASE TO EQUIP";
        float hintSize = 12f * unit;
        float hintWidth = UiPainter.MeasureLabel(context.Fonts.Label, Hint, hintSize, 0.22f);
        float hintY = centre.Y + outer + (44f * unit);
        Bgra32 hintColour = HudDrawContext.Fade(palette.Dim, open);
        context.Painter.DrawLabel(context.Fonts.Label, Hint, new UiVec2(centre.X - (hintWidth * 0.5f), hintY), hintSize, hintColour, 0.22f);
        float ruleY = hintY + (hintSize * 0.5f);
        context.Painter.FillRect(new UiRect(centre.X - (hintWidth * 0.5f) - (46f * unit), ruleY, 34f * unit, MathF.Max(1f, unit)), hintColour);
        context.Painter.FillRect(new UiRect(centre.X + (hintWidth * 0.5f) + (12f * unit), ruleY, 34f * unit, MathF.Max(1f, unit)), hintColour);
    }

    // The wheel's own backdrop, where the visor pass does not take the world out of time: blurred, darkened and pushed in, its
    // edges darker still, a little past the screen's edges so nothing unblurred shows round them.
    private static void Backdrop(HudDrawContext context, UiVec2 centre, float open)
    {
        float over = MathF.Max(context.Width, context.Height) * 0.06f;
        UiRect screen = new(-over, -over, context.Width + (over * 2f), context.Height + (over * 2f));
        context.Painter.DrawBackdrop(screen, BlurSigma * open, Bgra32.FromRgba(4, 6, 8, (byte)(105 * open)), 1f + (0.035f * open));
        float corner = MathF.Max(context.Width, context.Height) * 0.75f;
        context.Painter.FillRadialGradient(
            screen,
            centre,
            new UiVec2(corner, corner),
            [new UiGradientStop(0.25f, HudDrawContext.Fade(Bgra32.Black, 0f)), new UiGradientStop(1f, HudDrawContext.Fade(Bgra32.Black, 0.45f * open))]);
    }

    // The dial round the outside: fine ticks, every tenth longer, brightest where the mouse points; and beyond it a few thin
    // arcs turning slowly the other way.
    private static void Dial(HudDrawContext context, HudWheelView wheel, UiVec2 centre, float radius, float unit, float spin, float open)
    {
        HudPalette palette = context.Palette;
        float width = MathF.Max(1f, 1.3f * unit) / radius;
        for (int i = 0; i < DialTicks; i++)
        {
            float angle = (i * MathF.Tau / DialTicks) + spin;
            bool major = i % 10 == 0;
            float near = wheel.PointerAngle is { } pointing ? MathF.Exp(-MathF.Pow(Difference(angle, pointing) / 0.22f, 2f)) : 0f;
            float length = (major ? 9f : 4f) * unit * (1f + (0.8f * near));
            float alpha = ((major ? 0.45f : 0.22f) + (0.75f * near)) * open;
            Bgra32 colour = HudDrawContext.Fade(near > 0.3f ? palette.Highlight : palette.Primary, alpha);
            context.Painter.FillArc(centre, radius, radius + length, angle - width, angle + width, colour);
        }

        float turn = (context.Seconds * -0.18f) - spin;
        float orbit = radius + (20f * unit);
        float band = MathF.Max(1f, 1.4f * unit);
        Bgra32 arc = HudDrawContext.Fade(palette.Primary, 0.35f * open);
        for (int i = 0; i < 3; i++)
        {
            float start = turn + (i * MathF.Tau / 3f);
            context.Painter.FillArc(centre, orbit, orbit + band, start, start + 0.62f, arc);
            context.Painter.FillArc(centre, orbit, orbit + band, start + 0.72f, start + 0.78f, arc);
        }
    }

    // One sector, coming in after those before it round the ring: glass between two arcs, darker within and lit at its rim,
    // its weapon and rounds; the one pointed at lifts out, brightens, and throws a glow past the ring.
    private static void DrawSector(HudDrawContext context, HudWheelView wheel, int index, UiVec2 centre, float inner, float outer, float step, float unit, float spin, float open)
    {
        int count = wheel.Entries.Count;
        float arrive = Easing.Evaluate(Ease.OutCubic, Math.Clamp((open * 1.6f) - ((float)index / count * 0.6f), 0f, 1f));
        if (arrive <= 0f)
        {
            return;
        }

        bool hovered = index == wheel.Hovered;
        HudPalette palette = context.Palette;
        float middle = (index * step) + spin;
        float lift = (hovered ? Lift * unit : 0f) - ((1f - arrive) * 18f * unit);
        UiVec2 at = Point(centre, lift, middle);
        float trim = 4f * unit / ((inner + outer) * 0.5f);
        float from = middle - (step * 0.5f) + trim, to = middle + (step * 0.5f) - trim;
        float fade = arrive;

        if (hovered)
        {
            // The glow past the ring, and the glass lit through.
            context.Painter.FillArc(at, outer, outer + (34f * unit), from, to, HudDrawContext.Fade(palette.Highlight, 0.4f * fade), HudDrawContext.Fade(palette.Highlight, 0f));
            context.Painter.FillArc(at, inner, outer, from, to, HudDrawContext.Fade(palette.Primary, 0.22f * fade), HudDrawContext.Fade(palette.Primary, 0.6f * fade));
            context.Painter.FillArc(at, outer - MathF.Max(2f, 4f * unit), outer, from, to, HudDrawContext.Fade(palette.Highlight, fade));
            context.Painter.FillArc(at, inner, inner + MathF.Max(1f, 1.5f * unit), from, to, HudDrawContext.Fade(palette.Highlight, 0.8f * fade));
        }
        else
        {
            context.Painter.FillArc(at, inner, outer, from, to, HudDrawContext.Fade(palette.Glass, 0.95f * fade), HudDrawContext.Fade(palette.Primary, 0.14f * fade));
            context.Painter.FillArc(at, outer - MathF.Max(1f, 1.3f * unit), outer, from, to, HudDrawContext.Fade(palette.Primary, 0.45f * fade));
            context.Painter.FillArc(at, inner, inner + MathF.Max(1f, unit), from, to, HudDrawContext.Fade(palette.Primary, 0.2f * fade));
        }

        // The weapon in hand: a notch on the inner edge.
        if (index == wheel.Current)
        {
            float size = 6f * unit;
            Span<UiVec2> notch = stackalloc UiVec2[3];
            notch[0] = Point(at, inner + (4f * unit), middle - (size / inner));
            notch[1] = Point(at, inner + size + (4f * unit), middle);
            notch[2] = Point(at, inner + (4f * unit), middle + (size / inner));
            context.Painter.FillPolygon(notch, HudDrawContext.Fade(palette.Accent, fade));
        }

        // Its number, small, by its outer edge at its leading side.
        string number = (index + 1).ToString(CultureInfo.InvariantCulture);
        float numberSize = 10f * unit;
        UiVec2 numberAt = Point(at, outer - (16f * unit), from + (16f * unit / outer));
        context.Painter.DrawLabel(context.Fonts.Value, number, new UiVec2(numberAt.X - (numberSize * 0.3f), numberAt.Y - (numberSize * 0.5f)), numberSize, HudDrawContext.Fade(hovered ? palette.Highlight : palette.Dim, fade), 0f);

        Entry(context, wheel.Entries[index], Point(at, (inner + outer) * 0.5f, middle), outer - inner, step * ((inner + outer) * 0.5f), unit, hovered, fade);
    }

    // A weapon's picture, fitted to its sector, with a soft light behind it when pointed at, and its rounds under it; its name
    // where it has no picture.
    private static void Entry(HudDrawContext context, HudWheelEntry entry, UiVec2 at, float depth, float width, float unit, bool hovered, float fade)
    {
        HudPalette palette = context.Palette;
        Bgra32 colour = HudDrawContext.Fade(hovered ? palette.Highlight : palette.Primary, (hovered ? 1f : 0.7f) * fade);
        if (hovered)
        {
            context.Painter.FillArc(at, 0f, 40f * unit, 0f, MathF.Tau, HudDrawContext.Fade(palette.Primary, 0.35f * fade), HudDrawContext.Fade(palette.Primary, 0f));
        }

        float countSize = (hovered ? 14f : 12f) * unit;
        float boxWidth = Math.Min(width * 0.6f, 100f * unit), boxHeight = depth * 0.4f;
        float w = 0f, h = 0f;
        if (entry.Icon == HudIcon.None || !context.Icons.TryMeasure(entry.Icon, out w, out h) || w <= 0f || h <= 0f)
        {
            float nameSize = 14f * unit;
            float nameWidth = UiPainter.MeasureLabel(context.Fonts.Name, entry.Name, nameSize, 0.04f);
            context.Painter.DrawLabel(context.Fonts.Name, entry.Name, new UiVec2(at.X - (nameWidth * 0.5f), at.Y - nameSize), nameSize, colour, 0.04f);
        }
        else
        {
            float scale = MathF.Min(boxWidth / w, boxHeight / h) * (hovered ? 1.12f : 1f);
            context.DrawIcon(entry.Icon, at.X - (w * scale * 0.5f), at.Y - (h * scale * 0.5f) - (6f * unit), scale, colour);
        }

        if (entry.Count.Length > 0)
        {
            float countWidth = UiPainter.MeasureLabel(context.Fonts.Value, entry.Count, countSize, 0.06f);
            context.Painter.DrawLabel(context.Fonts.Value, entry.Count, new UiVec2(at.X - (countWidth * 0.5f), at.Y + (boxHeight * 0.5f) - (2f * unit)), countSize, colour, 0.06f);
        }
    }

    // The middle: a dark glass disc, a dashed ring turning slowly within its edge, and the weapon pointed at -- its picture large,
    // its name and its rounds.
    private static void Middle(HudDrawContext context, HudWheelView wheel, UiVec2 centre, float radius, float unit, float open)
    {
        HudPalette palette = context.Palette;
        context.Painter.FillArc(centre, 0f, radius, 0f, MathF.Tau, HudDrawContext.Fade(palette.Glass, open), HudDrawContext.Fade(palette.Glass, 0.9f * open));
        context.Painter.FillArc(centre, radius - MathF.Max(1f, 1.4f * unit), radius, 0f, MathF.Tau, HudDrawContext.Fade(palette.Primary, 0.5f * open));

        // The dashed ring within its edge, turning.
        float dashed = radius - (9f * unit);
        float turn = context.Seconds * 0.35f;
        const int Dashes = 36;
        Bgra32 dash = HudDrawContext.Fade(palette.Primary, 0.3f * open);
        for (int i = 0; i < Dashes; i++)
        {
            float start = turn + (i * MathF.Tau / Dashes);
            context.Painter.FillArc(centre, dashed, dashed + MathF.Max(1f, 1.2f * unit), start, start + (MathF.Tau / Dashes * 0.55f), dash);
        }

        const string Caption = "WEAPONS";
        float captionSize = 10f * unit;
        float captionWidth = UiPainter.MeasureLabel(context.Fonts.Label, Caption, captionSize, 0.35f);
        context.Painter.DrawLabel(context.Fonts.Label, Caption, new UiVec2(centre.X - (captionWidth * 0.5f), centre.Y - (radius * 0.7f)), captionSize, HudDrawContext.Fade(palette.Dim, open), 0.35f);

        if (wheel.Hovered < 0 || wheel.Hovered >= wheel.Entries.Count)
        {
            return;
        }

        HudWheelEntry entry = wheel.Entries[wheel.Hovered];
        if (entry.Icon != HudIcon.None && context.Icons.TryMeasure(entry.Icon, out float w, out float h) && w > 0f && h > 0f)
        {
            float scale = MathF.Min(radius * 1.05f / w, radius * 0.36f / h);
            context.DrawIcon(entry.Icon, centre.X - (w * scale * 0.5f), centre.Y - (radius * 0.48f), scale, HudDrawContext.Fade(palette.Highlight, 0.95f * open));
        }

        string name = entry.Name.ToUpper(CultureInfo.InvariantCulture);
        float nameSize = 17f * unit;
        float nameWidth = UiPainter.MeasureLabel(context.Fonts.Name, name, nameSize, 0.08f);
        if (nameWidth > radius * 1.6f)
        {
            nameSize *= radius * 1.6f / nameWidth;
            nameWidth = radius * 1.6f;
        }

        context.Painter.DrawLabel(context.Fonts.Name, name, new UiVec2(centre.X - (nameWidth * 0.5f), centre.Y + (2f * unit)), nameSize, HudDrawContext.Fade(palette.Highlight, open), 0.08f);
        if (entry.Count.Length > 0)
        {
            float countSize = 22f * unit;
            float countWidth = UiPainter.MeasureLabel(context.Fonts.Value, entry.Count, countSize, 0.04f);
            context.Painter.DrawLabel(context.Fonts.Value, entry.Count, new UiVec2(centre.X - (countWidth * 0.5f), centre.Y + nameSize + (8f * unit)), countSize, HudDrawContext.Fade(palette.Primary, open), 0.04f);
        }
    }

    // Where the mouse points: a short bright arc on the inner edge with a faint glow inside it, and a point out towards the sector.
    private static void Pointer(HudDrawContext context, HudWheelView wheel, UiVec2 centre, float radius, float unit, float open)
    {
        if (wheel.PointerAngle is not { } angle)
        {
            return;
        }

        Bgra32 colour = HudDrawContext.Fade(context.Palette.Highlight, open);
        float thickness = MathF.Max(2f, 3f * unit);
        context.Painter.FillArc(centre, radius - (thickness * 4f), radius - thickness, angle - 0.18f, angle + 0.18f, HudDrawContext.Fade(context.Palette.Highlight, 0f), HudDrawContext.Fade(context.Palette.Highlight, 0.3f * open));
        context.Painter.FillArc(centre, radius - thickness, radius, angle - 0.18f, angle + 0.18f, colour);

        float size = 7f * unit;
        Span<UiVec2> tip = stackalloc UiVec2[3];
        tip[0] = Point(centre, radius, angle - (size / radius));
        tip[1] = Point(centre, radius + size, angle);
        tip[2] = Point(centre, radius, angle + (size / radius));
        context.Painter.FillPolygon(tip, colour);
    }

    // How far apart two ways are, the short way round.
    private static float Difference(float a, float b)
    {
        float d = (a - b) % MathF.Tau;
        if (d > MathF.PI)
        {
            d -= MathF.Tau;
        }
        else if (d < -MathF.PI)
        {
            d += MathF.Tau;
        }

        return MathF.Abs(d);
    }

    // A point at a radius and a way clockwise from straight up.
    private static UiVec2 Point(UiVec2 centre, float radius, float angle) =>
        new(centre.X + (MathF.Sin(angle) * radius), centre.Y - (MathF.Cos(angle) * radius));
}
