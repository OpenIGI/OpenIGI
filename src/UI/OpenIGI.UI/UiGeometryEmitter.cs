using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI;

/// <summary>
/// The device-space half of <see cref="UiPainter"/>: trimming a quad to the clip, clipping a convex
/// polygon and emitting it as quads, and feathering its slanted edges.
/// </summary>
/// <remarks>
/// Everything here is already past the painter's transform and fade, so it needs nothing of the
/// painter's state but the renderer and the clip in force -- which is what makes it the same on every
/// backend, and assertable through a recording renderer.
/// </remarks>
internal static class UiGeometryEmitter
{
    /// <summary>Trims a destination rectangle to a clip, adjusting its source rectangle to match.</summary>
    /// <param name="clip">The clip rectangle.</param>
    /// <param name="dest">The destination, trimmed in place.</param>
    /// <param name="source">The source, trimmed in proportion in place.</param>
    /// <returns><see langword="false"/> when the destination lies wholly outside the clip.</returns>
    public static bool TryClip(UiRect clip, ref UiRect dest, ref UiRect source)
    {
        if (dest.Width <= 0f || dest.Height <= 0f)
        {
            return false;
        }

        float left = MathF.Max(dest.X, clip.X);
        float top = MathF.Max(dest.Y, clip.Y);
        float right = MathF.Min(dest.Right, clip.Right);
        float bottom = MathF.Min(dest.Bottom, clip.Bottom);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        // Nothing to trim: leave the source untouched so an unclipped draw is bit-for-bit what it was.
        if (left == dest.X && top == dest.Y && right == dest.Right && bottom == dest.Bottom)
        {
            return true;
        }

        float uPerPx = source.Width / dest.Width;
        float vPerPx = source.Height / dest.Height;
        source = new UiRect(
            source.X + ((left - dest.X) * uPerPx),
            source.Y + ((top - dest.Y) * vPerPx),
            (right - left) * uPerPx,
            (bottom - top) * vPerPx);
        dest = UiRect.FromEdges(left, top, right, bottom);
        return true;
    }

    /// <summary>Gets the unit normal of an edge pointing into the polygon it bounds.</summary>
    /// <param name="from">The edge's start.</param>
    /// <param name="to">The edge's end.</param>
    /// <param name="sign">The polygon's winding: 1 when its signed area is positive, otherwise -1.</param>
    /// <returns>The inward normal, or zero for a degenerate edge.</returns>
    public static UiVec2 InwardNormal(UiVec2 from, UiVec2 to, float sign)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 1e-6f)
        {
            return default;
        }

        // For a clockwise (Y-down) polygon the interior is on the right of each edge: (-dy, dx).
        return new UiVec2(-dy / length * sign, dx / length * sign);
    }

    /// <summary>Clips a device-space convex polygon and emits it as a fan of quads.</summary>
    /// <param name="renderer">The renderer the quads go to.</param>
    /// <param name="clip">The device-space clip rectangle.</param>
    /// <param name="polygon">The polygon, in device space.</param>
    /// <param name="image">The image the quads sample.</param>
    /// <param name="blend">How the quads combine with what is behind them.</param>
    public static void EmitPolygon(IUiRenderer renderer, UiRect clip, ReadOnlySpan<UiVertex> polygon, UiImage image, UiBlend blend)
    {
        scoped ReadOnlySpan<UiVertex> visible = polygon;
        Span<UiVertex> clipped = stackalloc UiVertex[UiGeometry.MaxClippedVertices];
        if (!UiGeometry.IsInside(polygon, clip))
        {
            int count = UiGeometry.ClipPolygon(polygon, clip, clipped);
            visible = clipped[..count];
        }

        int n = visible.Length;
        if (n < 3)
        {
            return;
        }

        if (n == 4)
        {
            renderer.DrawQuad(image, visible[0], visible[1], visible[2], visible[3], blend);
            return;
        }

        // A fan in pairs of triangles: each quad covers (0, i, i+1, i+2); a leftover triangle repeats its
        // last corner.
        for (int i = 1; i < n - 1; i += 2)
        {
            UiVertex c = visible[i + 1];
            UiVertex d = i + 2 < n ? visible[i + 2] : c;
            renderer.DrawQuad(image, visible[0], visible[i], c, d, blend);
        }
    }

    /// <summary>Adds a one-pixel transparent fringe outside every slanted edge of a device-space polygon.</summary>
    /// <param name="renderer">The renderer the fringe goes to.</param>
    /// <param name="clip">The device-space clip rectangle.</param>
    /// <param name="polygon">The polygon, in device space.</param>
    /// <param name="color">The polygon's colour, already faded; the fringe fades it to nothing.</param>
    /// <param name="blend">How the fringe combines with what is behind it.</param>
    public static void FeatherEdges(IUiRenderer renderer, UiRect clip, ReadOnlySpan<UiVertex> polygon, Bgra32 color, UiBlend blend)
    {
        int n = polygon.Length;
        Span<UiVec2> points = stackalloc UiVec2[n];
        for (int i = 0; i < n; i++)
        {
            points[i] = new UiVec2(polygon[i].X, polygon[i].Y);
        }

        float sign = UiGeometry.SignedArea2(points) >= 0f ? 1f : -1f;
        Bgra32 clear = color.WithAlpha(0);
        Span<UiVertex> quad = stackalloc UiVertex[4];
        for (int i = 0; i < n; i++)
        {
            UiVec2 a = points[i];
            UiVec2 b = points[(i + 1) % n];
            if (MathF.Abs(a.X - b.X) < 0.01f || MathF.Abs(a.Y - b.Y) < 0.01f)
            {
                continue;
            }

            UiVec2 inward = InwardNormal(a, b, sign);
            float ox = -inward.X;
            float oy = -inward.Y;
            quad[0] = new UiVertex(a.X, a.Y, 0f, 0f, color);
            quad[1] = new UiVertex(b.X, b.Y, 0f, 0f, color);
            quad[2] = new UiVertex(b.X + ox, b.Y + oy, 0f, 0f, clear);
            quad[3] = new UiVertex(a.X + ox, a.Y + oy, 0f, 0f, clear);
            EmitPolygon(renderer, clip, quad, UiImage.None, blend);
        }
    }
}
