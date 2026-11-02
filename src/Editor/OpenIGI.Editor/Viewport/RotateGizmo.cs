using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The rotate gizmo: three rings, one about each world axis, sized to stay the same on screen. Dragging a ring turns
/// the object about that axis by how far the pointer goes round the gizmo's centre, in 15° steps when snapping.
/// </summary>
internal sealed class RotateGizmo
{
    private const float RadiusPixels = 80f;
    private const float GrabPixels = 7f;
    private const int Segments = 64;
    private static readonly Vector3d[] Axes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

    private int _dragAxis = -1;
    private float _startAngle;
    private UiVec2 _centre;
    private Matrix3f _startOrientation;
    private double _sign;

    /// <summary>Gets whether a ring is being dragged.</summary>
    public bool IsDragging => _dragAxis >= 0;

    /// <summary>Gets the ring under the pointer, or -1.</summary>
    public int Hot { get; private set; } = -1;

    /// <summary>Gets or sets whether the turn snaps to 15° steps.</summary>
    public bool Snap { get; set; }

    /// <summary>Finds the ring under a point.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position, in metres.</param>
    /// <param name="point">The pointer.</param>
    /// <returns>The axis, or -1.</returns>
    public static int RingAt(in ViewportProjector projector, Vector3d origin, UiVec2 point)
    {
        double radius = projector.WorldSizeForPixels(origin, RadiusPixels);
        for (int axis = 0; axis < 3; axis++)
        {
            UiVec2? previous = null;
            for (int i = 0; i <= Segments; i++)
            {
                UiVec2? current = projector.TryProject(RingPoint(origin, axis, radius, i), out UiVec2 screen) ? screen : null;
                if (previous is { } a && current is { } b && UiVec2.DistanceToSegment(point, a, b) <= GrabPixels)
                {
                    return axis;
                }

                previous = current;
            }
        }

        return -1;
    }

    /// <summary>Updates the hover highlight.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    /// <param name="point">The pointer.</param>
    public void Hover(in ViewportProjector projector, Vector3d origin, UiVec2 point)
    {
        if (!IsDragging)
        {
            Hot = RingAt(projector, origin, point);
        }
    }

    /// <summary>Starts a drag if the press is on a ring.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    /// <param name="orientation">The object's orientation as the drag begins.</param>
    /// <param name="point">The press.</param>
    /// <returns>Whether a drag started.</returns>
    public bool Begin(in ViewportProjector projector, Vector3d origin, Matrix3f orientation, UiVec2 point)
    {
        int axis = RingAt(projector, origin, point);
        if (axis < 0 || !projector.TryProject(origin, out _centre))
        {
            return false;
        }

        _dragAxis = axis;
        Hot = axis;
        _startOrientation = orientation;
        _startAngle = MathF.Atan2(point.Y - _centre.Y, point.X - _centre.X);

        // Seen from the side the axis points to, a positive turn goes anticlockwise; the screen's y runs down, which
        // makes anticlockwise a falling angle. Seen from behind, the other way.
        Vector3d toEye = projector.Eye - origin;
        double facing = (Axes[axis].X * toEye.X) + (Axes[axis].Y * toEye.Y) + (Axes[axis].Z * toEye.Z);
        _sign = facing >= 0 ? -1.0 : 1.0;
        return true;
    }

    /// <summary>Follows the pointer.</summary>
    /// <param name="point">The pointer.</param>
    /// <returns>The object's new orientation.</returns>
    public Matrix3f Drag(UiVec2 point)
    {
        float angle = MathF.Atan2(point.Y - _centre.Y, point.X - _centre.X);
        double turn = Math.IEEERemainder(angle - _startAngle, Math.Tau) * _sign;
        if (Snap)
        {
            double step = Math.PI / 12.0;
            turn = Math.Round(turn / step) * step;
        }

        // A world-space turn W of an object whose orientation maps local to world through its transpose gives the
        // orientation O * Wᵀ; Wᵀ of a turn about an axis is the turn the other way.
        float back = (float)-turn;
        Matrix3f inverse = _dragAxis switch
        {
            0 => Matrix3f.RotationX(back),
            1 => Matrix3f.RotationY(back),
            _ => Matrix3f.RotationZ(back),
        };
        return _startOrientation * inverse;
    }

    /// <summary>Ends a drag.</summary>
    public void End() => _dragAxis = -1;

    /// <summary>Draws the rings.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme, for the axis colours.</param>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    public void Draw(UiPainter painter, EditorTheme theme, in ViewportProjector projector, Vector3d origin)
    {
        double radius = projector.WorldSizeForPixels(origin, RadiusPixels);
        for (int axis = 0; axis < 3; axis++)
        {
            bool hot = Hot == axis;
            Bgra32 color = hot ? Bgra32.FromRgb(255, 222, 90) : theme.AxisColor(axis);
            float thickness = theme.Px(hot ? 3f : 2f);
            UiVec2? previous = null;
            for (int i = 0; i <= Segments; i++)
            {
                UiVec2? current = projector.TryProject(RingPoint(origin, axis, radius, i), out UiVec2 screen) ? screen : null;
                if (previous is { } a && current is { } b)
                {
                    painter.StrokeLine(a, b, thickness, color);
                }

                previous = current;
            }
        }

        if (projector.TryProject(origin, out UiVec2 centre))
        {
            painter.FillDot(centre, theme.Px(3f), Bgra32.FromRgba(255, 255, 255, 220));
        }
    }

    private static Vector3d RingPoint(Vector3d origin, int axis, double radius, int index)
    {
        (double s, double c) = Math.SinCos(index * Math.Tau / Segments);
        return axis switch
        {
            0 => new Vector3d(origin.X, origin.Y + (c * radius), origin.Z + (s * radius)),
            1 => new Vector3d(origin.X + (c * radius), origin.Y, origin.Z + (s * radius)),
            _ => new Vector3d(origin.X + (c * radius), origin.Y + (s * radius), origin.Z),
        };
    }
}
