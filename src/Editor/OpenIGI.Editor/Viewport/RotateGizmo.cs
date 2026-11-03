using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The rotate gizmo, after the ones scene editors share (s&amp;box's among them): a ring about each axis, of the world or of the
/// object, and a white ring around them all that turns the object about the line of sight. Sized to stay the same on screen.
/// </summary>
/// <remarks>
/// At rest a ring shows only its half nearer the camera, so the three do not tangle into a ball; the one being dragged shows
/// whole, the others fade, and the angle turned is written beside the centre. Dragging turns by how far the pointer goes
/// round the centre, in the snap step when one is set.
/// </remarks>
internal sealed class RotateGizmo
{
    /// <summary>The ring number of the view ring.</summary>
    public const int ViewRing = 3;

    private static float RadiusPixels => 80f * GizmoSize.Scale;
    private static float ViewRadiusPixels => 96f * GizmoSize.Scale;
    private const float GrabPixels = 7f;
    private const int Segments = 64;

    private int _dragging = -1;
    private float _startAngle;
    private UiVec2 _centre;
    private Matrix3f _startOrientation;
    private Vector3d _axis;
    private double _sign;
    private double _turn;

    /// <summary>Gets whether a ring is being dragged.</summary>
    public bool IsDragging => _dragging >= 0;

    /// <summary>Gets the ring under the pointer: 0–2 an axis, 3 the view ring, -1 none.</summary>
    public int Hot { get; private set; } = -1;

    /// <summary>
    /// Gets or sets whether only the turn about the world's up axis is offered, for an object that stores a heading and
    /// nothing else (I.G.I. 2's cabinets).
    /// </summary>
    public bool UpOnly { get; set; }

    /// <summary>Gets or sets the snap step in degrees; zero for none.</summary>
    public double SnapDegrees { get; set; }

    /// <summary>Updates the hover highlight.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position, in metres.</param>
    /// <param name="frame">The axes the rings turn about: identity for the world's, the object's orientation for its own.</param>
    /// <param name="point">The pointer.</param>
    public void Hover(in ViewportProjector projector, Vector3d origin, Matrix3f frame, UiVec2 point)
    {
        if (!IsDragging)
        {
            Hot = RingAt(projector, origin, frame, point, UpOnly);
        }
    }

    /// <summary>Starts a drag if the press is on a ring.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    /// <param name="frame">The axes the rings turn about.</param>
    /// <param name="orientation">The object's orientation as the drag begins.</param>
    /// <param name="point">The press.</param>
    /// <returns>Whether a drag started.</returns>
    public bool Begin(in ViewportProjector projector, Vector3d origin, Matrix3f frame, Matrix3f orientation, UiVec2 point)
    {
        int ring = RingAt(projector, origin, frame, point, UpOnly);
        if (ring < 0 || !projector.TryProject(origin, out _centre))
        {
            return false;
        }

        Vector3d toEye = Unit(projector.Eye - origin);
        _axis = ring == ViewRing ? toEye : AxisOf(frame, ring);
        _dragging = ring;
        Hot = ring;
        _startOrientation = orientation;
        _startAngle = MathF.Atan2(point.Y - _centre.Y, point.X - _centre.X);
        _turn = 0.0;

        // Seen from the side the axis points to, a positive turn goes anticlockwise; the screen's y runs down, which makes
        // anticlockwise a falling angle. Seen from behind, the other way.
        _sign = Vector3d.Dot(_axis, toEye) >= 0 ? -1.0 : 1.0;
        return true;
    }

    /// <summary>Follows the pointer.</summary>
    /// <param name="point">The pointer.</param>
    /// <returns>The object's new orientation.</returns>
    public Matrix3f Drag(UiVec2 point)
    {
        float angle = MathF.Atan2(point.Y - _centre.Y, point.X - _centre.X);
        double turn = Math.IEEERemainder(angle - _startAngle, Math.Tau) * _sign;
        if (SnapDegrees > 0)
        {
            double step = SnapDegrees * Math.PI / 180.0;
            turn = Math.Round(turn / step) * step;
        }

        _turn = turn;

        // A world-space turn W of an object whose orientation maps local to world through its transpose gives the
        // orientation O * Wᵀ, and Wᵀ is the same turn the other way.
        return _startOrientation * Matrix3f.RotationAxis(_axis, (float)-turn);
    }

    /// <summary>Gets the world-space turn the drag has made so far: what carries the objects that turn with the one turned.</summary>
    public Matrix3f WorldTurn => Matrix3f.RotationAxis(_axis, (float)_turn);

    /// <summary>Ends a drag.</summary>
    public void End() => _dragging = -1;

    /// <summary>Draws the rings.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme, for the axis colours.</param>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position.</param>
    /// <param name="frame">The axes the rings turn about.</param>
    public void Draw(UiPainter painter, EditorTheme theme, in ViewportProjector projector, Vector3d origin, Matrix3f frame)
    {
        if (!projector.TryProject(origin, out UiVec2 centre))
        {
            return;
        }

        double radius = projector.WorldSizeForPixels(origin, RadiusPixels);
        Vector3d toEye = Unit(projector.Eye - origin);
        for (int ring = UpOnly ? 2 : 0; ring < 3; ring++)
        {
            bool dragged = _dragging == ring;
            bool faded = IsDragging && !dragged;
            bool hot = Hot == ring;
            Bgra32 colour = hot ? Bgra32.FromRgb(255, 222, 90) : theme.AxisColor(ring);
            colour = colour.WithAlpha(faded ? (byte)60 : (byte)230);
            DrawRing(painter, projector, origin, AxisOf(frame, ring), radius, toEye, wholeRing: dragged, theme.Px(hot ? 3f : 2f), colour);
        }

        // The view ring: a plain circle on screen around the others.
        if (UpOnly)
        {
            DrawReadout(painter, theme, centre);
            return;
        }

        bool viewHot = Hot == ViewRing;
        Bgra32 white = viewHot ? Bgra32.FromRgb(255, 222, 90) : Bgra32.FromRgba(255, 255, 255, IsDragging && _dragging != ViewRing ? (byte)50 : (byte)170);
        painter.FillArc(centre, ViewRadiusPixels - theme.Px(viewHot ? 3f : 2f), ViewRadiusPixels, 0f, MathF.Tau, white);
        painter.FillDot(centre, theme.Px(3f), Bgra32.FromRgba(255, 255, 255, 220));
        DrawReadout(painter, theme, centre);
    }

    // The angle turned so far, beside the gizmo while dragging.
    private void DrawReadout(UiPainter painter, EditorTheme theme, UiVec2 centre)
    {
        if (IsDragging)
        {
            string degrees = (_turn * 180.0 / Math.PI).ToString("0.#", CultureInfo.InvariantCulture) + "°";
            UiVec2 at = new(centre.X + theme.Px(14f), centre.Y - theme.Px(26f));
            float width = theme.Font.MeasureWidth(degrees, theme.TextSize) + theme.Px(12f);
            EditorDraw.Fill(painter, theme, new UiRect(at.X, at.Y, width, theme.ControlHeight), Bgra32.FromRgba(0, 0, 0, 170));
            EditorDraw.Label(painter, theme, degrees, new UiRect(at.X + theme.Px(6f), at.Y, width, theme.ControlHeight), theme.Text);
        }
    }

    private static int RingAt(in ViewportProjector projector, Vector3d origin, Matrix3f frame, UiVec2 point, bool upOnly)
    {
        if (!projector.TryProject(origin, out UiVec2 centre))
        {
            return -1;
        }

        double radius = projector.WorldSizeForPixels(origin, RadiusPixels);
        Vector3d toEye = Unit(projector.Eye - origin);
        for (int ring = upOnly ? 2 : 0; ring < 3; ring++)
        {
            Vector3d axis = AxisOf(frame, ring);
            (Vector3d u, Vector3d v) = Basis(axis);
            UiVec2? previous = null;
            for (int i = 0; i <= Segments; i++)
            {
                Vector3d offset = Offset(u, v, radius, i);
                UiVec2? current = Vector3d.Dot(offset, toEye) >= -0.05 * radius && projector.TryProject(origin + offset, out UiVec2 screen) ? screen : null;
                if (previous is { } a && current is { } b && UiVec2.DistanceToSegment(point, a, b) <= GrabPixels)
                {
                    return ring;
                }

                previous = current;
            }
        }

        float fromCentre = UiVec2.Distance(point, centre);
        return !upOnly && MathF.Abs(fromCentre - ViewRadiusPixels) <= GrabPixels ? ViewRing : -1;
    }

    private static void DrawRing(UiPainter painter, in ViewportProjector projector, Vector3d origin, Vector3d axis, double radius, Vector3d toEye, bool wholeRing, float thickness, Bgra32 colour)
    {
        (Vector3d u, Vector3d v) = Basis(axis);
        UiVec2? previous = null;
        for (int i = 0; i <= Segments; i++)
        {
            Vector3d offset = Offset(u, v, radius, i);
            bool shown = wholeRing || Vector3d.Dot(offset, toEye) >= -0.05 * radius;
            UiVec2? current = shown && projector.TryProject(origin + offset, out UiVec2 screen) ? screen : null;
            if (previous is { } a && current is { } b)
            {
                painter.StrokeLine(a, b, thickness, colour);
            }

            previous = current;
        }
    }

    // An object's own axis in the world: its orientation maps local to world through its transpose, so local axis i lands on
    // the orientation's row i.
    private static Vector3d AxisOf(Matrix3f frame, int axis)
    {
        Vector3f row = axis switch { 0 => frame.Row1, 1 => frame.Row2, _ => frame.Row3 };
        return Unit(new Vector3d(row.X, row.Y, row.Z));
    }

    // Two unit vectors spanning the plane the ring about an axis lies in.
    private static (Vector3d U, Vector3d V) Basis(Vector3d axis)
    {
        Vector3d helper = Math.Abs(axis.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        Vector3d u = Unit(Vector3d.Cross(axis, helper));
        return (u, Vector3d.Cross(axis, u));
    }

    private static Vector3d Offset(Vector3d u, Vector3d v, double radius, int index)
    {
        (double s, double c) = Math.SinCos(index * Math.Tau / Segments);
        return ((u * c) + (v * s)) * radius;
    }

    private static Vector3d Unit(Vector3d v)
    {
        double length = v.Length;
        return length > 1e-9 ? v / length : new Vector3d(0, 0, 1);
    }
}
