using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// A detection mark for each soldier who has the player in sight, after Splinter Cell: Blacklist's: a thin, flat chevron over
/// his head that fills from its ends to its peak as he makes the player out -- white while he only looks, doubled and orange
/// under "WARNING" as he grows suspicious, red under a flickering "DETECTED" once he is sure. One out of view is a thin arc on
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

        UiVec2 aim = new(context.Width * 0.5f, context.Height * 0.5f);
        foreach (HudThreat threat in model.Threats)
        {
            if (threat.Mark is not { } mark || threat.Awareness <= 0.01f)
            {
                continue;
            }

            float awareness = Math.Clamp(threat.Awareness, 0f, 1f);
            (UiVec2 at, bool inView, float towards) = Place(projector, mark, context.Px(EdgeMargin));
            if (inView)
            {
                Chevron(context, at, awareness);
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
    // to the peak by how sure he is; suspicious, a second chevron over it and "WARNING"; sure, red, glowing, "DETECTED".
    private static void Chevron(HudDrawContext context, UiVec2 over, float awareness)
    {
        bool sure = awareness >= Sure, warned = awareness >= Suspicious;
        float beat = sure ? 0.5f + (0.5f * MathF.Sin(context.Seconds * 10f)) : 0f;
        float opacity = context.Reactive.Appear;
        Bgra32 colour = Colour(awareness, context.Palette.Danger);
        float half = context.Px(52f), rise = context.Px(13f), thickness = MathF.Max(1.5f, context.Px(2.6f));
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

        // Its word over it; "DETECTED" flickers, a slice of it slipping now and then.
        string word = sure ? "DETECTED" : "WARNING";
        float size = context.Px(24f);
        float width = UiPainter.MeasureLabel(context.Fonts.Name, word, size, 0.08f);
        UiVec2 at = new(over.X - (width * 0.5f), peak2.Y - size - context.Px(6f));
        if (sure)
        {
            float tick = MathF.Floor(context.Seconds * 18f);
            float slip = (Hash(tick) > 0.7f ? (Hash(tick + 3.1f) - 0.5f) : 0f) * context.Px(10f);
            context.Painter.DrawLabel(context.Fonts.Name, word, new UiVec2(at.X + slip + context.Px(2.5f), at.Y), size, HudDrawContext.Fade(colour, 0.35f * opacity), 0.08f);
            context.Painter.DrawLabel(context.Fonts.Name, word, new UiVec2(at.X - slip, at.Y), size, HudDrawContext.Fade(colour, (0.85f + (0.15f * beat)) * opacity), 0.08f);
        }
        else
        {
            context.Painter.DrawLabel(context.Fonts.Name, word, at, size, HudDrawContext.Fade(colour, opacity), 0.08f);
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

        context.Painter.StrokeLine(left, Lerp(left, peak, share), thickness, colour);
        context.Painter.StrokeLine(right, Lerp(right, peak, share), thickness, colour);
    }

    // White while he looks, orange as he grows suspicious, the theme's danger once he is sure.
    private static Bgra32 Colour(float awareness, Bgra32 danger) =>
        awareness >= Sure ? danger : awareness >= Suspicious ? Warning : Looking;

    private static UiVec2 Lerp(UiVec2 a, UiVec2 b, float t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

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
