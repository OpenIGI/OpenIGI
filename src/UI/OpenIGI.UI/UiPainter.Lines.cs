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

    /// <summary>Fills a round dot with its edge smoothed.</summary>
    /// <param name="centre">Its centre, in pixels.</param>
    /// <param name="radius">Its radius.</param>
    /// <param name="color">Its colour.</param>
    /// <param name="blend">How it combines with what is behind it.</param>
    public void FillDot(UiVec2 centre, float radius, Bgra32 color, UiBlend blend = UiBlend.AlphaBlend) =>
        FillArc(centre, 0f, radius, 0f, MathF.Tau, color, blend);
}
