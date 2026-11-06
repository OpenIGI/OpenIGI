using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// A flat grid fixed in the world at one height, laid out under the camera and fading with distance from it: what snapping
/// lands on, drawn where the work is.
/// </summary>
/// <remarks>
/// <para>
/// Nothing about it follows where the camera looks, so it does not slide or jump as the view turns: its lines sit on whole
/// multiples of the step in world space, it is centred under the camera, and its spacing comes from the camera's height
/// above it alone -- the snap step doubled until about forty lines span what can be seen from that height. Every fourth line
/// is stronger, counted from the world's origin so the strong lines stay put too.
/// </para>
/// <para>
/// Lines are drawn in short pieces so the parts behind the camera drop out rather than folding across the view.
/// </para>
/// </remarks>
internal static class ViewportGrid
{
    private const int HalfLines = 24;
    private const int Pieces = 24;
    private const int MajorEvery = 4;

    /// <summary>Draws the grid.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="projector">The view.</param>
    /// <param name="height">The grid's height, in world metres; nothing is drawn when the camera is not above it.</param>
    /// <param name="snapStep">The snap step, in metres.</param>
    public static void Draw(UiPainter painter, EditorTheme theme, in ViewportProjector projector, double height, double snapStep)
    {
        Vector3d eye = projector.Eye;
        double above = eye.Z - height;
        if (above <= 0.05)
        {
            return;
        }

        double step = Math.Max(0.01, snapStep);
        while (step * HalfLines < Math.Max(8.0, above * 4.0))
        {
            step *= 2.0;
        }

        double extent = step * HalfLines;
        double major = step * MajorEvery;
        double originX = Math.Round(eye.X / major) * major;
        double originY = Math.Round(eye.Y / major) * major;
        Vector3d centre = new(eye.X, eye.Y, height);
        for (int line = -HalfLines; line <= HalfLines; line++)
        {
            bool strong = (long)Math.Round((originX / step) + line) % MajorEvery == 0;
            DrawLine(painter, theme, projector, centre, new Vector3d(originX + (line * step), originY - extent, height), new Vector3d(0, extent * 2.0, 0), extent, strong);
            strong = (long)Math.Round((originY / step) + line) % MajorEvery == 0;
            DrawLine(painter, theme, projector, centre, new Vector3d(originX - extent, originY + (line * step), height), new Vector3d(extent * 2.0, 0, 0), extent, strong);
        }
    }

    private static void DrawLine(UiPainter painter, EditorTheme theme, in ViewportProjector projector, Vector3d centre, Vector3d start, Vector3d along, double extent, bool strong)
    {
        UiVec2? previous = null;
        for (int i = 0; i <= Pieces; i++)
        {
            Vector3d at = start + (along * ((double)i / Pieces));
            UiVec2? current = projector.TryProject(at, out UiVec2 screen) ? screen : null;
            if (previous is { } a && current is { } b)
            {
                // Faded by how far the piece is from under the camera, so the grid has no hard edge.
                double reach = new Vector3d(at.X - centre.X, at.Y - centre.Y, 0).Length / extent;
                double fade = Math.Clamp(1.0 - reach, 0.0, 1.0);
                byte alpha = (byte)((strong ? 70 : 34) * fade * fade);
                if (alpha > 2)
                {
                    painter.StrokeLine(a, b, theme.Hairline, Bgra32.FromRgba(255, 255, 255, alpha));
                }
            }

            previous = current;
        }
    }
}
