using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI;

/// <content>Strokes and dots with every edge smoothed, for fine marks that sit off the pixel grid: an aiming mark, a tick.</content>
public sealed partial class UiPainter
{
    /// <summary>Strokes a straight line with every edge smoothed, however it lies against the pixel grid.</summary>
    /// <param name="from">Where it starts, in pixels.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="width">How thick it is, in pixels; its smoothing adds about a pixel round it.</param>
    /// <param name="color">Its colour.</param>
    /// <param name="blend">How it combines with what is behind it.</param>
    /// <remarks>
    /// The line is a quad half a pixel inside its width, its edges faded out over a pixel beyond, so its weight reads true and
    /// a stroke that moves a fraction of a pixel a frame glides rather than steps.
    /// </remarks>
    public void StrokeSoft(UiVec2 from, UiVec2 to, float width, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-4f || width <= 0f)
        {
            return;
        }

        float half = MathF.Max(0.05f, (width * 0.5f) - 0.5f);
        float nx = -dy / length * half, ny = dx / length * half;

        // The ends pulled in by the same half pixel, so the feather keeps the line its length.
        float ex = dx / length * 0.5f, ey = dy / length * 0.5f;
        Bgra32 faded = Fade(color);
        Span<UiVertex> quad = stackalloc UiVertex[4];
        quad[0] = Vertex(from.X + ex + nx, from.Y + ey + ny, faded);
        quad[1] = Vertex(to.X - ex + nx, to.Y - ey + ny, faded);
        quad[2] = Vertex(to.X - ex - nx, to.Y - ey - ny, faded);
        quad[3] = Vertex(from.X + ex - nx, from.Y + ey - ny, faded);
        EmitPolygon(quad, UiImage.None, blend);
        UiGeometryEmitter.FeatherEdges(Renderer, Clip, quad, faded, blend, allEdges: true);
    }

    /// <summary>
    /// Draws a smoothed line through points as one strip, its joints mitred so neighbouring segments share their corners: a
    /// curve drawn as many segments shows no seam or gap where they meet.
    /// </summary>
    /// <param name="points">The points, in order; at least two.</param>
    /// <param name="width">The line's width, in pixels.</param>
    /// <param name="color">Its colour.</param>
    /// <param name="blend">How it combines with what is behind it.</param>
    public void StrokeSoftPolyline(ReadOnlySpan<UiVec2> points, float width, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend)
    {
        if (points.Length < 2 || width <= 0f)
        {
            return;
        }

        float half = MathF.Max(0.05f, (width * 0.5f) - 0.5f);
        Span<UiVec2> normals = points.Length <= 256 ? stackalloc UiVec2[points.Length] : new UiVec2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            UiVec2 before = SegmentNormal(points[Math.Max(0, i - 1)], points[Math.Max(1, i)]);
            UiVec2 after = SegmentNormal(points[Math.Min(points.Length - 2, i)], points[Math.Min(points.Length - 1, i + 1)]);
            UiVec2 sum = new(before.X + after.X, before.Y + after.Y);
            float length = MathF.Sqrt((sum.X * sum.X) + (sum.Y * sum.Y));
            UiVec2 miter = length < 1e-4f ? before : new UiVec2(sum.X / length, sum.Y / length);

            // Kept as wide as the line along the bend, but never a spike at a sharp turn.
            float cos = MathF.Max(0.35f, (miter.X * after.X) + (miter.Y * after.Y));
            normals[i] = new UiVec2(miter.X / cos, miter.Y / cos);
        }

        Bgra32 faded = Fade(color);
        Bgra32 clear = faded.WithAlpha(0);
        Span<UiVertex> quad = stackalloc UiVertex[4];
        for (int i = 0; i + 1 < points.Length; i++)
        {
            UiVec2 a = points[i], b = points[i + 1];
            UiVec2 na = normals[i], nb = normals[i + 1];
            if (MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) < 1e-4f)
            {
                continue;
            }

            quad[0] = Vertex(a.X + (na.X * half), a.Y + (na.Y * half), faded);
            quad[1] = Vertex(b.X + (nb.X * half), b.Y + (nb.Y * half), faded);
            quad[2] = Vertex(b.X - (nb.X * half), b.Y - (nb.Y * half), faded);
            quad[3] = Vertex(a.X - (na.X * half), a.Y - (na.Y * half), faded);
            EmitPolygon(quad, UiImage.None, blend);

            // A pixel of feather along each side, fading out; the ends meet the next segment's, so none are feathered.
            for (int side = -1; side <= 1; side += 2)
            {
                float inner = half * side, outer = (half + 1f) * side;
                quad[0] = Vertex(a.X + (na.X * inner), a.Y + (na.Y * inner), faded);
                quad[1] = Vertex(b.X + (nb.X * inner), b.Y + (nb.Y * inner), faded);
                quad[2] = Vertex(b.X + (nb.X * outer), b.Y + (nb.Y * outer), clear);
                quad[3] = Vertex(a.X + (na.X * outer), a.Y + (na.Y * outer), clear);
                EmitPolygon(quad, UiImage.None, blend);
            }
        }
    }

    // The unit normal of a segment, or straight up for none.
    private static UiVec2 SegmentNormal(UiVec2 from, UiVec2 to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        return length < 1e-4f ? new UiVec2(0f, -1f) : new UiVec2(-dy / length, dx / length);
    }

    /// <summary>Fills a round dot with its edge smoothed.</summary>
    /// <param name="centre">Its centre, in pixels.</param>
    /// <param name="radius">Its radius.</param>
    /// <param name="color">Its colour.</param>
    /// <param name="blend">How it combines with what is behind it.</param>
    public void FillDot(UiVec2 centre, float radius, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend) =>
        FillArc(centre, 0f, radius, 0f, MathF.Tau, color, blend);
}
