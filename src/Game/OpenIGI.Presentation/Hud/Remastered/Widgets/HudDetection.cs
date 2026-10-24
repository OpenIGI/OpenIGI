using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// A detection mark for each soldier who has the player in sight, after Splinter Cell: Blacklist's: a thin, flat chevron over
/// his head that fills from its ends to its peak as he makes the player out -- white while he only looks, doubled and orange
/// as he grows suspicious, red once he is sure -- smaller the further off he is; marks that would crowd one another give way to
/// the surest. As the alarm rises, the surest soldier in view carries the word for a moment: "WARNING", then "DETECTED". One out of view is a thin arc on
/// a ring round the aim, on his side, with a point out towards him, in the same colours.
/// </summary>
/// <remarks>
/// Only those who see the player now are marked (the titles' <c>Threats</c>): the arc tells the player what the player could
/// know -- someone is looking -- and nothing of those behind walls.
/// </remarks>
public static class HudDetection
{
    /// <summary>How far in from the screen's edge a soldier counts as in view, in design pixels.</summary>
    public const float EdgeMargin = 56f;

    /// <summary>How far round the aim an arc for a soldier out of view stands, in design pixels.</summary>
    public const float RingRadius = 150f;

    /// <summary>How sure a soldier must be for his arc to turn red, nought to one.</summary>
    public const float Sure = 0.95f;

    /// <summary>How many soldiers are marked at most.</summary>
    public const int MaximumMarks = 24;

    /// <summary>How close two marks may come, in design pixels, before the less sure is left out.</summary>
    public const float CrowdRadius = 70f;

    /// <summary>How suspicious a soldier must be for his chevron to double and warn, nought to one.</summary>
    public const float Suspicious = 0.7f;

    private static readonly Bgra32 Looking = Bgra32.FromRgba(245, 247, 250, 255);
    private static readonly Bgra32 Warning = Bgra32.FromRgba(255, 150, 60, 255);

    /// <summary>Where a soldier is on the screen and whether he is in view, and the way to him from the middle in radians.</summary>
    /// <param name="projector">The player's view.</param>
    /// <param name="mark">The point over the soldier, in world units.</param>
    /// <param name="margin">How far in from the edge he must be to count in view, in pixels.</param>
    /// <returns>His place over him when in view, the way to him from the middle of the screen either way.</returns>
    public static (UiVec2 At, bool InView, float Towards) Place(HudProjector projector, Vector3d mark, float margin)
    {
        bool ahead = projector.TryProject(mark, out UiVec2 at);
        float cx = projector.Width * 0.5f, cy = projector.Height * 0.5f;
        bool inView = ahead && at.X > margin && at.X < projector.Width - margin && at.Y > margin && at.Y < projector.Height - margin;

        // Behind, he projects to his own side through the depth's size; only the height is turned down, where "behind" reads.
        float dx = at.X - cx, dy = ahead ? at.Y - cy : MathF.Abs(cy - at.Y) + (projector.Height * 0.25f);
        return (at, inView, MathF.Atan2(dy, dx));
    }

    /// <summary>Draws the arcs.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="model">The frame's model.</param>
    /// <param name="projector">The player's view.</param>
    public static void Draw(HudDrawContext context, HudModel model, HudProjector projector)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        if (model.Show == HudParts.None)
        {
            return;
        }

        // The surest first, so where marks would crowd one another the surest is the one drawn, and the word goes over him alone.
        int count = Math.Min(model.Threats.Count, MaximumMarks);
        Span<int> order = stackalloc int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        for (int i = 1; i < count; i++)
        {
            for (int j = i; j > 0 && model.Threats[order[j]].Awareness > model.Threats[order[j - 1]].Awareness; j--)
            {
                (order[j], order[j - 1]) = (order[j - 1], order[j]);
            }
        }

        UiVec2 aim = new(context.Width * 0.5f, context.Height * 0.5f);
        Span<UiVec2> drawn = stackalloc UiVec2[count];
        int marks = 0;
        bool worded = false;
        float crowd = context.Px(CrowdRadius);
        foreach (int index in order)
        {
            HudThreat threat = model.Threats[index];
            if (threat.Mark is not { } mark || threat.Awareness <= 0.01f)
            {
                continue;
            }

            float awareness = Math.Clamp(threat.Awareness, 0f, 1f);
            (UiVec2 at, bool inView, float towards) = Place(projector, mark, context.Px(EdgeMargin));
            if (inView)
            {
                bool crowded = false;
                for (int k = 0; k < marks && !crowded; k++)
                {
                    crowded = MathF.Abs(drawn[k].X - at.X) < crowd && MathF.Abs(drawn[k].Y - at.Y) < crowd * 0.6f;
                }

                if (crowded)
                {
                    continue;
                }

                drawn[marks++] = at;

                // Smaller the further off he is, so a crowd far away is not a wall of marks.
                double dx = mark.X - projector.Eye.X, dy = mark.Y - projector.Eye.Y, dz = mark.Z - projector.Eye.Z;
                float metres = (float)(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / 4096.0);
                float size = Math.Clamp(1.15f - (metres / 70f), 0.55f, 1f);
                Chevron(context, at, awareness, size, word: !worded);
                worded = true;
            }
            else
            {
                // On the ring round the aim, its middle on the way to him and its point out towards him.
                float radius = context.Px(RingRadius);
                float span = MathF.Min(0.55f, context.Px(70f) / MathF.Max(1f, radius));
                Band(context, aim, radius, towards, span, awareness);
            }
        }
    }

    // The chevron over a soldier: two thin legs rising from its ends to a low peak over him, a faint track filled from the ends
    // to the peak by how sure he is; suspicious, a second chevron over it; sure, red and glowing. The surest soldier in view
    // carries the word -- "WARNING", "DETECTED" -- and only for a moment after the alarm rises (HudReactiveState.DetectionWord).
    private static void Chevron(HudDrawContext context, UiVec2 over, float awareness, float size, bool word)
    {
        bool sure = awareness >= Sure, warned = awareness >= Suspicious;
        float beat = sure ? 0.5f + (0.5f * MathF.Sin(context.Seconds * 10f)) : 0f;
        float opacity = context.Reactive.Appear;
        Bgra32 colour = Colour(awareness, context.Palette.Danger);
        float half = context.Px(44f) * size, rise = context.Px(11f) * size, thickness = MathF.Max(1.5f, context.Px(2.4f) * size);
        UiVec2 left = new(over.X - half, over.Y), peak = new(over.X, over.Y - rise), right = new(over.X + half, over.Y);

        if (sure)
        {
            Legs(context, left, peak, right, 1f, thickness * 3.5f, HudDrawContext.Fade(colour, (0.12f + (0.12f * beat)) * opacity));
        }

        Legs(context, left, peak, right, 1f, thickness, HudDrawContext.Fade(Bgra32.Black, 0.35f * opacity));
        Legs(context, left, peak, right, 1f, MathF.Max(1f, thickness * 0.45f), HudDrawContext.Fade(colour, 0.35f * opacity));
        Legs(context, left, peak, right, awareness, thickness, HudDrawContext.Fade(colour, (0.6f + (0.4f * awareness)) * opacity));

        if (!warned)
        {
            return;
        }

        // Doubled: a second, narrower chevron over the first, converging on it.
        float lift = context.Px(10f);
        UiVec2 left2 = new(over.X - (half * 0.62f), over.Y - lift), peak2 = new(over.X, over.Y - rise - (lift * 0.55f)), right2 = new(over.X + (half * 0.62f), over.Y - lift);
        Legs(context, left2, peak2, right2, 1f, thickness, HudDrawContext.Fade(colour, 0.9f * opacity));

        // Its word over it, for a moment after the alarm rises: "DETECTED" flickers, a slice of it slipping now and then.
        float shown = word ? context.Reactive.DetectionWord : 0f;
        if (shown <= 0.01f)
        {
            return;
        }

        string text = sure ? "DETECTED" : "WARNING";
        float textSize = context.Px(20f);
        float width = UiPainter.MeasureLabel(context.Fonts.Name, text, textSize, 0.12f);
        UiVec2 at = new(over.X - (width * 0.5f), peak2.Y - textSize - context.Px(6f));
        float fade = opacity * shown;
        if (sure)
        {
            float tick = MathF.Floor(context.Seconds * 18f);
            float slip = (Hash(tick) > 0.7f ? (Hash(tick + 3.1f) - 0.5f) : 0f) * context.Px(8f);
            context.Painter.DrawLabel(context.Fonts.Name, text, new UiVec2(at.X + slip + context.Px(2f), at.Y), textSize, HudDrawContext.Fade(colour, 0.35f * fade), 0.12f);
            context.Painter.DrawLabel(context.Fonts.Name, text, new UiVec2(at.X - slip, at.Y), textSize, HudDrawContext.Fade(colour, (0.85f + (0.15f * beat)) * fade), 0.12f);
        }
        else
        {
            context.Painter.DrawLabel(context.Fonts.Name, text, at, textSize, HudDrawContext.Fade(colour, fade), 0.12f);
        }
    }

    // A chevron's two legs, each drawn from its outer end towards the peak, the share given.
    private static void Legs(HudDrawContext context, UiVec2 left, UiVec2 peak, UiVec2 right, float share, float thickness, Bgra32 colour)
    {
        share = Math.Clamp(share, 0f, 1f);
        if (share <= 0f)
        {
            return;
        }

        Line(context, left, Interpolate.Lerp(left, peak, share), thickness, colour);
        Line(context, right, Interpolate.Lerp(right, peak, share), thickness, colour);
    }

    // A thick line as a quad with its long edges smoothed.
    private static void Line(HudDrawContext context, UiVec2 from, UiVec2 to, float thickness, Bgra32 colour)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-3f)
        {
            return;
        }

        float nx = -dy / length * thickness * 0.5f, ny = dx / length * thickness * 0.5f;
        Span<UiVec2> quad = stackalloc UiVec2[4];
        quad[0] = new UiVec2(from.X + nx, from.Y + ny);
        quad[1] = new UiVec2(to.X + nx, to.Y + ny);
        quad[2] = new UiVec2(to.X - nx, to.Y - ny);
        quad[3] = new UiVec2(from.X - nx, from.Y - ny);
        context.Painter.FillPolygon(quad, colour);
    }

    // White while he looks, orange as he grows suspicious, the theme's danger once he is sure.
    private static Bgra32 Colour(float awareness, Bgra32 danger) =>
        awareness >= Sure ? danger : awareness >= Suspicious ? Warning : Looking;

    // A steady pseudo-random number in [0, 1) for a number, so a flicker repeats the same on each frame of its tick.
    private static float Hash(float n)
    {
        float x = MathF.Sin(n * 12.9898f) * 43758.5453f;
        return x - MathF.Floor(x);
    }

    // The band on the ring round the aim: a faint track the whole span, the fill growing from both ends towards the middle, and
    // a point at the middle out towards the soldier. Sure, it is solid red, beats and glows.
    private static void Band(HudDrawContext context, UiVec2 centre, float radius, float middle, float span, float awareness)
    {
        bool sure = awareness >= Sure;
        float beat = sure ? 0.5f + (0.5f * MathF.Sin(context.Seconds * 10f)) : 0f;
        float opacity = context.Reactive.Appear;
        Bgra32 colour = Colour(awareness, context.Palette.Danger);
        float thickness = MathF.Max(2f, context.Px(3.2f)) * (1f + (0.25f * beat));
        float half = span * 0.5f;

        // A soft glow under it once he is sure.
        if (sure)
        {
            Segment(context, centre, radius, middle - half, middle + half, thickness * 3.2f, HudDrawContext.Fade(colour, (0.14f + (0.14f * beat)) * opacity));
        }

        // The track, faint, the whole span.
        Segment(context, centre, radius, middle - half, middle + half, thickness, HudDrawContext.Fade(Bgra32.Black, 0.4f * opacity));
        Segment(context, centre, radius, middle - half, middle + half, MathF.Max(1f, thickness * 0.35f), HudDrawContext.Fade(colour, 0.3f * opacity));

        // The fill, from both ends towards the middle, brighter the surer he is.
        float filled = half * awareness;
        Bgra32 fill = HudDrawContext.Fade(colour, (0.55f + (0.45f * awareness)) * opacity);
        Segment(context, centre, radius, middle - half, middle - half + filled, thickness, fill);
        Segment(context, centre, radius, middle + half - filled, middle + half, thickness, fill);

        // The point at its middle, shown once he is half sure, and whole once he is.
        if (awareness >= 0.5f)
        {
            float size = context.Px(6f) * (sure ? 1f + (0.2f * beat) : 0.8f);
            float edge = radius + (thickness * 0.5f);
            UiVec2 tip = At(centre, edge + size, middle);
            float side = size / MathF.Max(1f, radius);
            context.Painter.FillTriangle(At(centre, edge, middle - side), tip, At(centre, edge, middle + side), HudDrawContext.Fade(colour, (sure ? 1f : 0.7f) * opacity));
        }
    }

    // A piece of a ring between two angles, from the screen's right, turning down.
    private static void Segment(HudDrawContext context, UiVec2 centre, float radius, float from, float to, float thickness, Bgra32 colour)
    {
        if (to <= from)
        {
            return;
        }

        int pieces = Math.Max(1, (int)MathF.Ceiling((to - from) / 0.06f));
        float inner = radius - (thickness * 0.5f), outer = radius + (thickness * 0.5f);
        for (int i = 0; i < pieces; i++)
        {
            float a0 = from + ((to - from) * i / pieces);
            float a1 = from + ((to - from) * (i + 1) / pieces);
            context.Painter.FillQuad(At(centre, inner, a0), At(centre, outer, a0), At(centre, outer, a1), At(centre, inner, a1), colour);
        }
    }

    private static UiVec2 At(UiVec2 centre, float radius, float angle) =>
        new(centre.X + (MathF.Cos(angle) * radius), centre.Y + (MathF.Sin(angle) * radius));
}
