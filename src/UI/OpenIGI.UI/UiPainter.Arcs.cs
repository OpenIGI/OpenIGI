using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI;

/// <content>Bands of a ring -- arcs with a thickness -- filled with edges smoothed all round.</content>
public sealed partial class UiPainter
{
    /// <summary>How far apart, in pixels along the outer edge, an arc's steps fall: fine enough that the edge reads as a curve.</summary>
    private const float ArcStepPixels = 5f;

    /// <summary>
    /// Fills a band of a ring between two radii and two angles, its every edge -- both arcs and both ends -- feathered by a pixel
    /// so it is smooth however it is turned; a band wider than a half turn fills as truly as a narrow one.
    /// </summary>
    /// <param name="centre">The ring's centre, in pixels.</param>
    /// <param name="inner">The band's inner radius, in pixels; nought fills a slice of a disc.</param>
    /// <param name="outer">The band's outer radius, in pixels.</param>
    /// <param name="from">Where it starts, in radians clockwise from straight up.</param>
    /// <param name="to">Where it ends, in radians clockwise from straight up; past <paramref name="from"/>.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    public void FillArc(UiVec2 centre, float inner, float outer, float from, float to, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend) =>
        FillArc(centre, inner, outer, from, to, color, color, blend);

    /// <summary>
    /// Fills a band of a ring shaded from one colour at its inner edge to another at its outer, its every edge feathered as
    /// <see cref="FillArc(UiVec2, float, float, float, float, Bgra32, UiBlend)"/>'s is: a glow fading outward, say, or glass lit
    /// at its rim.
    /// </summary>
    /// <param name="centre">The ring's centre, in pixels.</param>
    /// <param name="inner">The band's inner radius, in pixels.</param>
    /// <param name="outer">The band's outer radius, in pixels.</param>
    /// <param name="from">Where it starts, in radians clockwise from straight up.</param>
    /// <param name="to">Where it ends, in radians clockwise from straight up.</param>
    /// <param name="innerColor">The colour at its inner edge.</param>
    /// <param name="outerColor">The colour at its outer edge.</param>
    /// <param name="blend">How the fill combines with what is behind it.</param>
    public void FillArc(UiVec2 centre, float inner, float outer, float from, float to, Bgra32 innerColor, Bgra32 outerColor, UiBlend blend = UiBlend.AlphaBlend)
    {
        if (to <= from || outer <= inner || outer <= 0f)
        {
            return;
        }

        inner = MathF.Max(0f, inner);
        Bgra32 inside = Fade(innerColor), outside = Fade(outerColor);
        int steps = Math.Clamp((int)MathF.Ceiling((to - from) * outer / ArcStepPixels), 1, 512);
        Span<UiVertex> quad = stackalloc UiVertex[4];
        float feather = 1f;
        for (int i = 0; i < steps; i++)
        {
            float a0 = from + ((to - from) * i / steps);
            float a1 = from + ((to - from) * (i + 1) / steps);

            // The body, then a pixel's fade off its outer edge and, where it has one, its inner.
            quad[0] = Vertex(Around(centre, inner, a0), inside);
            quad[1] = Vertex(Around(centre, outer, a0), outside);
            quad[2] = Vertex(Around(centre, outer, a1), outside);
            quad[3] = Vertex(Around(centre, inner, a1), inside);
            EmitPolygon(quad, UiImage.None, blend);

            quad[0] = Vertex(Around(centre, outer, a0), outside);
            quad[1] = Vertex(Around(centre, outer + feather, a0), outside.WithAlpha(0));
            quad[2] = Vertex(Around(centre, outer + feather, a1), outside.WithAlpha(0));
            quad[3] = Vertex(Around(centre, outer, a1), outside);
            EmitPolygon(quad, UiImage.None, blend);

            if (inner > feather)
            {
                quad[0] = Vertex(Around(centre, inner - feather, a0), inside.WithAlpha(0));
                quad[1] = Vertex(Around(centre, inner, a0), inside);
                quad[2] = Vertex(Around(centre, inner, a1), inside);
                quad[3] = Vertex(Around(centre, inner - feather, a1), inside.WithAlpha(0));
                EmitPolygon(quad, UiImage.None, blend);
            }
        }

        // Its two ends, each faded a pixel back along the way it does not run.
        if (to - from < MathF.Tau - 1e-3f)
        {
            FeatherEnd(centre, inner, outer, from, -1f, inside, outside, blend);
            FeatherEnd(centre, inner, outer, to, 1f, inside, outside, blend);
        }
    }

    // A pixel's fade off one end of a band, outward along the ring's turn.
    private void FeatherEnd(UiVec2 centre, float inner, float outer, float angle, float outward, Bgra32 inside, Bgra32 outside, UiBlend blend)
    {
        UiVec2 a = Around(centre, inner, angle), b = Around(centre, outer, angle);
        UiVec2 step = new(MathF.Cos(angle) * outward, MathF.Sin(angle) * outward);
        Span<UiVertex> quad = stackalloc UiVertex[4];
        quad[0] = Vertex(a, inside);
        quad[1] = Vertex(b, outside);
        quad[2] = Vertex(new UiVec2(b.X + step.X, b.Y + step.Y), outside.WithAlpha(0));
        quad[3] = Vertex(new UiVec2(a.X + step.X, a.Y + step.Y), inside.WithAlpha(0));
        EmitPolygon(quad, UiImage.None, blend);
    }

    // A point at a radius and a way clockwise from straight up.
    private static UiVec2 Around(UiVec2 centre, float radius, float angle) =>
        new(centre.X + (MathF.Sin(angle) * radius), centre.Y - (MathF.Cos(angle) * radius));

    private UiVertex Vertex(UiVec2 point, Bgra32 color) => Vertex(point.X, point.Y, color);
}
