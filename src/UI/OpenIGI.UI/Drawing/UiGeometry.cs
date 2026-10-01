using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Drawing;

/// <summary>The CPU geometry every renderer shares: polygon clipping and colour interpolation.</summary>
/// <remarks>
/// Kept on the CPU and out of the renderers on purpose. The game's sprite batch has no scissor and the
/// launcher's canvas does; doing the clip here, once, means a cut-corner panel half-scrolled out of a
/// list is trimmed to the same pixel on both.
/// </remarks>
public static class UiGeometry
{
    /// <summary>The most vertices a clipped polygon can have: the input's plus one per clip edge.</summary>
    public const int MaxClippedVertices = 64;

    /// <summary>Linearly interpolates two colours channel by channel.</summary>
    /// <param name="from">The colour at <paramref name="t"/> = 0.</param>
    /// <param name="to">The colour at <paramref name="t"/> = 1.</param>
    /// <param name="t">The blend factor, clamped to 0..1.</param>
    /// <returns>The interpolated colour.</returns>
    public static Bgra32 Lerp(Bgra32 from, Bgra32 to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Bgra32(
            (byte)MathF.Round(from.B + ((to.B - from.B) * t)),
            (byte)MathF.Round(from.G + ((to.G - from.G) * t)),
            (byte)MathF.Round(from.R + ((to.R - from.R) * t)),
            (byte)MathF.Round(from.A + ((to.A - from.A) * t)));
    }

    /// <summary>Samples a gradient's colour at a position along it.</summary>
    /// <param name="stops">The stops, in increasing offset order.</param>
    /// <param name="t">The position along the gradient, 0 to 1.</param>
    /// <returns>The colour there; the end stops' colours beyond the ends.</returns>
    public static Bgra32 Sample(ReadOnlySpan<UiGradientStop> stops, float t)
    {
        if (stops.Length == 0)
        {
            return Bgra32.Transparent;
        }

        if (t <= stops[0].Offset)
        {
            return stops[0].Color;
        }

        for (int i = 1; i < stops.Length; i++)
        {
            if (t <= stops[i].Offset)
            {
                float span = stops[i].Offset - stops[i - 1].Offset;
                float local = span <= 0f ? 1f : (t - stops[i - 1].Offset) / span;
                return Lerp(stops[i - 1].Color, stops[i].Color, local);
            }
        }

        return stops[^1].Color;
    }

    /// <summary>Clips a convex polygon to an axis-aligned rectangle, interpolating texture coordinates and colours.</summary>
    /// <param name="input">The polygon's vertices, in order.</param>
    /// <param name="clip">The rectangle to clip to.</param>
    /// <param name="output">Receives the clipped polygon; must hold <see cref="MaxClippedVertices"/>.</param>
    /// <returns>The clipped polygon's vertex count; fewer than three means nothing is visible.</returns>
    /// <remarks>Sutherland–Hodgman against the four edges in turn, which is exact for a convex input.</remarks>
    public static int ClipPolygon(ReadOnlySpan<UiVertex> input, UiRect clip, Span<UiVertex> output)
    {
        if (input.Length < 3 || clip.IsEmpty)
        {
            return 0;
        }

        Span<UiVertex> a = stackalloc UiVertex[MaxClippedVertices];
        Span<UiVertex> b = stackalloc UiVertex[MaxClippedVertices];
        int count = Math.Min(input.Length, MaxClippedVertices - 4);
        input[..count].CopyTo(a);

        count = ClipEdge(a[..count], b, clip.X, axis: 0, keepGreater: true);
        count = ClipEdge(b[..count], a, clip.Right, axis: 0, keepGreater: false);
        count = ClipEdge(a[..count], b, clip.Y, axis: 1, keepGreater: true);
        count = ClipEdge(b[..count], a, clip.Bottom, axis: 1, keepGreater: false);

        a[..count].CopyTo(output);
        return count;
    }

    /// <summary>Gets a value indicating whether a polygon lies entirely inside a rectangle.</summary>
    /// <param name="points">The polygon's vertices.</param>
    /// <param name="clip">The rectangle.</param>
    /// <returns><see langword="true"/> when no vertex is outside.</returns>
    public static bool IsInside(ReadOnlySpan<UiVertex> points, UiRect clip)
    {
        foreach (UiVertex v in points)
        {
            if (v.X < clip.X || v.X > clip.Right || v.Y < clip.Y || v.Y > clip.Bottom)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets twice the polygon's signed area: positive when its vertices run clockwise on screen.</summary>
    /// <param name="points">The polygon's vertices.</param>
    /// <returns>Twice the signed area, in Y-down screen space.</returns>
    public static float SignedArea2(ReadOnlySpan<UiVec2> points)
    {
        float area = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            UiVec2 p = points[i];
            UiVec2 q = points[(i + 1) % points.Length];
            area += (p.X * q.Y) - (q.X * p.Y);
        }

        return area;
    }

    private static int ClipEdge(ReadOnlySpan<UiVertex> input, Span<UiVertex> output, float plane, int axis, bool keepGreater)
    {
        int written = 0;
        if (input.Length == 0)
        {
            return 0;
        }

        UiVertex previous = input[^1];
        bool previousInside = Inside(previous, plane, axis, keepGreater);
        foreach (UiVertex current in input)
        {
            bool currentInside = Inside(current, plane, axis, keepGreater);
            if (currentInside != previousInside && written < output.Length)
            {
                output[written++] = Intersect(previous, current, plane, axis);
            }

            if (currentInside && written < output.Length)
            {
                output[written++] = current;
            }

            previous = current;
            previousInside = currentInside;
        }

        return written;
    }

    private static bool Inside(in UiVertex v, float plane, int axis, bool keepGreater)
    {
        float value = axis == 0 ? v.X : v.Y;
        return keepGreater ? value >= plane : value <= plane;
    }

    private static UiVertex Intersect(in UiVertex p, in UiVertex q, float plane, int axis)
    {
        float pv = axis == 0 ? p.X : p.Y;
        float qv = axis == 0 ? q.X : q.Y;
        float t = qv == pv ? 0f : (plane - pv) / (qv - pv);
        return new UiVertex(
            p.X + ((q.X - p.X) * t),
            p.Y + ((q.Y - p.Y) * t),
            p.U + ((q.U - p.U) * t),
            p.V + ((q.V - p.V) * t),
            Lerp(p.Color, q.Color, t));
    }
}
