using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The orientation widget in a corner of the viewport, as scene editors have it: the world's three axes as the camera sees
/// them, coloured, with a dot at each end. Clicking a dot looks along that axis toward the middle of the view; dragging the
/// widget orbits.
/// </summary>
internal sealed class OrientationGizmo
{
    private const float RadiusPixels = 34f;
    private const float DotPixels = 8f;

    /// <summary>Gets the dot under the pointer: 0–2 the positive X, Y, Z, 3–5 the negative ones, -1 none.</summary>
    public int Hot { get; private set; } = -1;

    /// <summary>Gets the widget's area in a view, in its top-right corner.</summary>
    /// <param name="view">The view's rectangle.</param>
    /// <param name="theme">The theme.</param>
    /// <returns>The area.</returns>
    public static UiRect Area(UiRect view, EditorTheme theme)
    {
        float size = (RadiusPixels + DotPixels + 6f) * 2f;
        return new UiRect(view.Right - size - theme.Px(10f), view.Y + theme.ControlHeight + theme.Px(18f), size, size);
    }

    /// <summary>Updates the hover highlight.</summary>
    /// <param name="area">The widget's area.</param>
    /// <param name="yaw">The camera's yaw.</param>
    /// <param name="pitch">The camera's pitch.</param>
    /// <param name="point">The pointer.</param>
    public void Hover(UiRect area, float yaw, float pitch, UiVec2 point) => Hot = DotAt(area, yaw, pitch, point);

    /// <summary>Gets the direction the camera should look along for a dot: from the dot's end of the axis toward the middle.</summary>
    /// <param name="dot">The dot.</param>
    /// <returns>The direction.</returns>
    public static Vector3d LookFor(int dot) => -Axis(dot);

    /// <summary>Finds the dot under a point.</summary>
    /// <param name="area">The widget's area.</param>
    /// <param name="yaw">The camera's yaw.</param>
    /// <param name="pitch">The camera's pitch.</param>
    /// <param name="point">The point.</param>
    /// <returns>The dot, or -1.</returns>
    public static int DotAt(UiRect area, float yaw, float pitch, UiVec2 point)
    {
        int best = -1;
        float bestDistance = DotPixels + 2f;
        for (int dot = 0; dot < 6; dot++)
        {
            (UiVec2 at, _) = Place(area, yaw, pitch, dot);
            float distance = UiVec2.Distance(at, point);
            if (distance <= bestDistance)
            {
                best = dot;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Draws the widget.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="area">The widget's area.</param>
    /// <param name="yaw">The camera's yaw.</param>
    /// <param name="pitch">The camera's pitch.</param>
    /// <param name="hovered">Whether the pointer is over the widget.</param>
    public void Draw(UiPainter painter, EditorTheme theme, UiRect area, float yaw, float pitch, bool hovered)
    {
        UiVec2 centre = area.Center;
        painter.FillDot(centre, area.Width * 0.5f, Bgra32.FromRgba(0, 0, 0, hovered ? (byte)110 : (byte)60));

        // Far dots first, so the near ones draw over them.
        int[] order = [.. Enumerable.Range(0, 6).OrderBy(dot => Place(area, yaw, pitch, dot).Depth)];
        foreach (int dot in order)
        {
            (UiVec2 at, double depth) = Place(area, yaw, pitch, dot);
            bool positive = dot < 3;
            bool hot = dot == Hot;
            Bgra32 colour = theme.AxisColor(dot % 3);
            if (positive)
            {
                painter.StrokeLine(centre, at, theme.Px(2f), colour.WithAlpha(200));
            }

            float radius = hot ? DotPixels + 2f : positive ? DotPixels : DotPixels * 0.7f;
            painter.FillDot(at, radius, hot ? Bgra32.FromRgb(255, 222, 90) : positive ? colour : colour.WithAlpha((byte)(depth > 0 ? 90 : 150)));
            if (positive)
            {
                string label = dot switch { 0 => "X", 1 => "Y", _ => "Z" };
                float width = theme.SmallFont.MeasureWidth(label, theme.SmallTextSize);
                float height = theme.SmallFont.LineHeight(theme.SmallTextSize);
                painter.DrawText(theme.SmallFont, label, new UiVec2(MathF.Round(at.X - (width * 0.5f)), MathF.Round(at.Y - (height * 0.5f))), theme.SmallTextSize, Bgra32.FromRgb(20, 20, 20));
            }
        }
    }

    // Where a dot lands on the widget, and how far it sits away from the camera (positive behind the middle).
    private static (UiVec2 At, double Depth) Place(UiRect area, float yaw, float pitch, int dot)
    {
        (Vector3d forward, Vector3d right, Vector3d up) = EditorCamera.Axes(yaw, pitch);
        Vector3d axis = Axis(dot);
        UiVec2 centre = area.Center;
        return (new UiVec2(centre.X + (float)(Vector3d.Dot(axis, right) * RadiusPixels), centre.Y - (float)(Vector3d.Dot(axis, up) * RadiusPixels)), Vector3d.Dot(axis, forward));
    }

    private static Vector3d Axis(int dot)
    {
        double sign = dot < 3 ? 1.0 : -1.0;
        return (dot % 3) switch
        {
            0 => new Vector3d(sign, 0, 0),
            1 => new Vector3d(0, sign, 0),
            _ => new Vector3d(0, 0, sign),
        };
    }
}
