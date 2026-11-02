using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The move gizmo: three axis arrows and a centre handle that moves in the view plane, sized to stay the same on
/// screen at any distance, dragged with optional snapping.
/// </summary>
internal sealed class TranslateGizmo
{
    private const float ArmPixels = 90f;
    private const float GrabPixels = 7f;
    private static readonly Vector3d[] Axes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

    private int _dragAxis = -2;
    private Vector3d _dragStart;
    private UiVec2 _pointerStart;
    private UiVec2 _axisScreen;
    private double _pixelsPerMetre;

    /// <summary>Gets whether a handle is being dragged.</summary>
    public bool IsDragging => _dragAxis > -2;

    /// <summary>Gets the handle under the pointer: 0–2 an axis, -1 the centre, -2 none.</summary>
    public int Hot { get; private set; } = -2;

    /// <summary>Gets or sets the snap step in metres; zero for none.</summary>
    public double Snap { get; set; }

    /// <summary>Finds the handle under a point.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position, in metres.</param>
    /// <param name="point">The pointer.</param>
    /// <returns>The handle, as <see cref="Hot"/> numbers it.</returns>
    public static int HandleAt(in ViewportProjector projector, Vector3d origin, UiVec2 point)
    {
        if (!projector.TryProject(origin, out UiVec2 centre))
        {
            return -2;
        }

        if (UiVec2.Distance(point, centre) <= GrabPixels * 1.4f)
        {
            return -1;
        }

        double arm = projector.WorldSizeForPixels(origin, ArmPixels);
        for (int axis = 0; axis < 3; axis++)
        {
            if (projector.TryProject(origin + (Axes[axis] * arm), out UiVec2 tip) && UiVec2.DistanceToSegment(point, centre, tip) <= GrabPixels)
            {
                return axis;
            }
        }

        return -2;
    }

    /// <summary>Updates the hover highlight.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    /// <param name="point">The pointer.</param>
    public void Hover(in ViewportProjector projector, Vector3d origin, UiVec2 point)
    {
        if (!IsDragging)
        {
            Hot = HandleAt(projector, origin, point);
        }
    }

    /// <summary>Starts a drag if the press is on a handle.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    /// <param name="point">The press.</param>
    /// <returns>Whether a drag started.</returns>
    public bool Begin(in ViewportProjector projector, Vector3d origin, UiVec2 point)
    {
        int handle = HandleAt(projector, origin, point);
        if (handle == -2 || !projector.TryProject(origin, out UiVec2 centre))
        {
            return false;
        }

        _dragAxis = handle;
        Hot = handle;
        _dragStart = origin;
        _pointerStart = point;
        double arm = projector.WorldSizeForPixels(origin, ArmPixels);
        if (handle >= 0 && projector.TryProject(origin + (Axes[handle] * arm), out UiVec2 tip))
        {
            UiVec2 along = tip - centre;
            float length = MathF.Max(1f, along.Length);
            _axisScreen = new UiVec2(along.X / length, along.Y / length);
            _pixelsPerMetre = length / arm;
        }
        else
        {
            _pixelsPerMetre = ArmPixels / arm;
        }

        return true;
    }

    /// <summary>Follows the pointer.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="point">The pointer.</param>
    /// <param name="fine">Whether to move at a tenth of the speed.</param>
    /// <returns>The new position, in metres.</returns>
    public Vector3d Drag(in ViewportProjector projector, UiVec2 point, bool fine)
    {
        double scale = fine ? 0.1 : 1.0;
        UiVec2 delta = new(point.X - _pointerStart.X, point.Y - _pointerStart.Y);
        Vector3d moved;
        if (_dragAxis >= 0)
        {
            double metres = ((delta.X * _axisScreen.X) + (delta.Y * _axisScreen.Y)) / _pixelsPerMetre * scale;
            moved = _dragStart + (Axes[_dragAxis] * SnapValue(metres));
        }
        else
        {
            // The centre handle slides over the ground plane, along the view's horizontal axes.
            Vector3d forward = _dragStart - projector.Eye;
            forward = new Vector3d(forward.X, forward.Y, 0);
            double length = Math.Max(1e-6, forward.Length);
            forward = forward * (1.0 / length);
            Vector3d right = new(forward.Y, -forward.X, 0);
            double dx = delta.X / _pixelsPerMetre * scale;
            double dy = -delta.Y / _pixelsPerMetre * scale;
            Vector3d offset = (right * dx) + (forward * dy);
            moved = _dragStart + new Vector3d(SnapValue(offset.X), SnapValue(offset.Y), 0);
        }

        return moved;
    }

    /// <summary>Ends a drag.</summary>
    public void End() => _dragAxis = -2;

    /// <summary>Draws the gizmo.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme, for the axis colours.</param>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    public void Draw(UiPainter painter, EditorTheme theme, in ViewportProjector projector, Vector3d origin)
    {
        if (!projector.TryProject(origin, out UiVec2 centre))
        {
            return;
        }

        double arm = projector.WorldSizeForPixels(origin, ArmPixels);
        float thickness = theme.Px(2.5f);
        for (int axis = 0; axis < 3; axis++)
        {
            if (!projector.TryProject(origin + (Axes[axis] * arm), out UiVec2 tip))
            {
                continue;
            }

            bool hot = Hot == axis;
            Bgra32 color = hot ? Bgra32.FromRgb(255, 222, 90) : theme.AxisColor(axis);
            painter.StrokeLine(centre, tip, hot ? thickness * 1.4f : thickness, color);
            painter.FillDot(tip, theme.Px(hot ? 6f : 5f), color);
        }

        Bgra32 centreColor = Hot == -1 ? Bgra32.FromRgb(255, 222, 90) : Bgra32.FromRgba(255, 255, 255, 220);
        float half = theme.Px(Hot == -1 ? 6f : 5f);
        painter.FillRect(new UiRect(centre.X - half, centre.Y - half, half * 2f, half * 2f), centreColor);
    }

    private double SnapValue(double value) => Snap > 0 ? Math.Round(value / Snap) * Snap : value;
}
