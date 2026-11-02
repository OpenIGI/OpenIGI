using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The move gizmo: three axis arrows, three squares that move on two axes at once, and a centre handle that drops what
/// it moves onto the surface under the pointer. Sized to stay the same on screen at any distance.
/// </summary>
/// <remarks>
/// <para>
/// A drag follows the pointer exactly, as scene editors do (s&amp;box's among them): the pointer's ray is met with a plane and
/// the object moves by how far the meeting point has gone. An arrow's plane contains the arrow and faces the camera as
/// squarely as it can; a square's plane is its own; the centre, with no surface under the pointer, slides in the plane
/// facing the camera. Nothing is measured in pixels, so perspective cannot make the object drift from under the pointer.
/// </para>
/// <para>
/// An arrow pointing nearly at the camera, or a square seen nearly edge on, is left out: dragging it would mean a
/// pixel moving the object a long way.
/// </para>
/// </remarks>
internal sealed class TranslateGizmo
{
    /// <summary>The handle number for the centre.</summary>
    public const int Centre = -1;

    /// <summary>The handle number for none.</summary>
    public const int None = -2;

    private const float ArmPixels = 90f;
    private const float GrabPixels = 8f;
    private const double SquareStart = 0.22;
    private const double SquareSize = 0.2;
    private static readonly Vector3d[] Axes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];

    private int _dragging = None;
    private Vector3d _start;
    private Vector3d _startHit;
    private Vector3d _planeNormal;
    private Func<UiVec2, Vector3d?>? _surface;

    /// <summary>Gets whether a handle is being dragged.</summary>
    public bool IsDragging => _dragging != None;

    /// <summary>Gets the handle under the pointer: 0–2 an axis, 3–5 the square across the other two, -1 the centre, -2 none.</summary>
    public int Hot { get; private set; } = None;

    /// <summary>Gets or sets the snap step in metres; zero for none.</summary>
    public double Snap { get; set; }

    /// <summary>Updates the hover highlight.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="origin">The gizmo's position, in metres.</param>
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
    /// <param name="surface">Finds the surface under a point of the view, for the centre handle; ignores what is moved.</param>
    /// <returns>Whether a drag started.</returns>
    public bool Begin(in ViewportProjector projector, Vector3d origin, UiVec2 point, Func<UiVec2, Vector3d?>? surface = null)
    {
        int handle = HandleAt(projector, origin, point);
        if (handle == None || !projector.TryRay(point, out Vector3d ray))
        {
            return false;
        }

        Vector3d toEye = Unit(projector.Eye - origin);
        _planeNormal = handle switch
        {
            >= 0 and <= 2 => Unit(toEye - (Axes[handle] * Vector3d.Dot(toEye, Axes[handle]))),
            >= 3 => Axes[handle - 3],
            _ => toEye,
        };

        if (!Meet(projector.Eye, ray, origin, _planeNormal, out _startHit))
        {
            return false;
        }

        _dragging = handle;
        Hot = handle;
        _start = origin;
        _surface = surface;
        return true;
    }

    /// <summary>Follows the pointer.</summary>
    /// <param name="projector">The view.</param>
    /// <param name="point">The pointer.</param>
    /// <param name="toggleSnap">Whether snapping is turned the other way for this drag (Ctrl).</param>
    /// <returns>The new position, in metres.</returns>
    public Vector3d Drag(in ViewportProjector projector, UiVec2 point, bool toggleSnap)
    {
        double step = Snap > 0 ? (toggleSnap ? 0.0 : Snap) : (toggleSnap ? 0.5 : 0.0);

        // The centre lands on whatever surface is under the pointer, when there is one.
        if (_dragging == Centre && _surface?.Invoke(point) is { } landed)
        {
            return step > 0 ? SnapEach(landed, step) : landed;
        }

        if (!projector.TryRay(point, out Vector3d ray) || !Meet(projector.Eye, ray, _start, _planeNormal, out Vector3d hit))
        {
            return _start;
        }

        Vector3d delta = hit - _startHit;
        if (_dragging is >= 0 and <= 2)
        {
            Vector3d axis = Axes[_dragging];
            delta = axis * Snapped(Vector3d.Dot(delta, axis), step);
        }
        else
        {
            delta = step > 0 ? SnapEach(delta, step) : delta;
        }

        return _start + delta;
    }

    /// <summary>Ends a drag.</summary>
    public void End()
    {
        _dragging = None;
        _surface = null;
    }

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
        Vector3d toEye = Unit(projector.Eye - origin);

        // The squares first, so the arrows draw over their corners.
        for (int plane = 0; plane < 3; plane++)
        {
            if (Square(projector, origin, arm, plane, toEye) is not { } quad)
            {
                continue;
            }

            bool hot = Hot == plane + 3;
            Bgra32 colour = Shade(theme.AxisColor(plane), hot);
            painter.FillQuad(quad[0], quad[1], quad[2], quad[3], colour.WithAlpha((byte)(hot ? 150 : 70)));
            for (int i = 0; i < 4; i++)
            {
                painter.StrokeLine(quad[i], quad[(i + 1) % 4], theme.Px(1.5f), colour);
            }
        }

        float thickness = theme.Px(3f);
        for (int axis = 0; axis < 3; axis++)
        {
            if (Culled(Axes[axis], toEye) || !projector.TryProject(origin + (Axes[axis] * arm), out UiVec2 tip))
            {
                continue;
            }

            bool hot = Hot == axis;
            Bgra32 colour = Shade(theme.AxisColor(axis), hot);
            UiVec2 along = tip - centre;
            float length = MathF.Max(1f, along.Length);
            UiVec2 unit = new(along.X / length, along.Y / length);
            UiVec2 side = new(-unit.Y, unit.X);
            float head = theme.Px(hot ? 15f : 13f);
            float halfWidth = theme.Px(hot ? 6f : 5f);
            UiVec2 neck = new(tip.X - (unit.X * head), tip.Y - (unit.Y * head));
            painter.StrokeLine(centre, neck, hot ? thickness * 1.3f : thickness, colour);
            painter.FillTriangle(tip, new UiVec2(neck.X + (side.X * halfWidth), neck.Y + (side.Y * halfWidth)), new UiVec2(neck.X - (side.X * halfWidth), neck.Y - (side.Y * halfWidth)), colour);
        }

        // The centre: a ring that fills in when it is the handle under the pointer.
        bool centreHot = Hot == Centre;
        float radius = theme.Px(centreHot ? 8f : 7f);
        painter.FillDot(centre, radius, Bgra32.FromRgba(255, 255, 255, (byte)(centreHot ? 230 : 60)));
        painter.FillArc(centre, radius - theme.Px(2f), radius, 0f, MathF.Tau, Bgra32.FromRgba(255, 255, 255, 230));
    }

    private static int HandleAt(in ViewportProjector projector, Vector3d origin, UiVec2 point)
    {
        if (!projector.TryProject(origin, out UiVec2 centre))
        {
            return None;
        }

        if (UiVec2.Distance(point, centre) <= GrabPixels * 1.3f)
        {
            return Centre;
        }

        double arm = projector.WorldSizeForPixels(origin, ArmPixels);
        Vector3d toEye = Unit(projector.Eye - origin);
        int best = None;
        float bestDistance = GrabPixels;
        for (int axis = 0; axis < 3; axis++)
        {
            if (!Culled(Axes[axis], toEye) && projector.TryProject(origin + (Axes[axis] * arm), out UiVec2 tip))
            {
                float distance = UiVec2.DistanceToSegment(point, centre, tip);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = axis;
                }
            }
        }

        if (best != None)
        {
            return best;
        }

        for (int plane = 0; plane < 3; plane++)
        {
            if (Square(projector, origin, arm, plane, toEye) is { } quad && Inside(quad, point))
            {
                return plane + 3;
            }
        }

        return None;
    }

    // The square across the two axes other than one, near the centre, as its four corners on screen; none when it is seen
    // nearly edge on or any corner is behind the eye.
    private static UiVec2[]? Square(in ViewportProjector projector, Vector3d origin, double arm, int normalAxis, Vector3d toEye)
    {
        if (Math.Abs(Vector3d.Dot(Axes[normalAxis], toEye)) < 0.15)
        {
            return null;
        }

        Vector3d a = Axes[(normalAxis + 1) % 3] * arm;
        Vector3d b = Axes[(normalAxis + 2) % 3] * arm;
        Vector3d corner = origin + (a * SquareStart) + (b * SquareStart);
        UiVec2[] quad = new UiVec2[4];
        Vector3d[] points = [corner, corner + (a * SquareSize), corner + (a * SquareSize) + (b * SquareSize), corner + (b * SquareSize)];
        for (int i = 0; i < 4; i++)
        {
            if (!projector.TryProject(points[i], out quad[i]))
            {
                return null;
            }
        }

        return quad;
    }

    // An arrow pointing within about ten degrees of the eye cannot be dragged meaningfully.
    private static bool Culled(Vector3d axis, Vector3d toEye) => Math.Abs(Vector3d.Dot(axis, toEye)) > 0.985;

    private static bool Inside(UiVec2[] quad, UiVec2 point)
    {
        bool inside = false;
        for (int i = 0, j = quad.Length - 1; i < quad.Length; j = i++)
        {
            if ((quad[i].Y > point.Y) != (quad[j].Y > point.Y)
                && point.X < ((quad[j].X - quad[i].X) * (point.Y - quad[i].Y) / (quad[j].Y - quad[i].Y)) + quad[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    // Where a ray meets a plane through a point; false when it runs along the plane or meets it behind the eye.
    private static bool Meet(Vector3d eye, Vector3d ray, Vector3d through, Vector3d normal, out Vector3d hit)
    {
        hit = default;
        double facing = Vector3d.Dot(ray, normal);
        if (Math.Abs(facing) < 1e-6)
        {
            return false;
        }

        double t = Vector3d.Dot(through - eye, normal) / facing;
        if (t <= 0)
        {
            return false;
        }

        hit = eye + (ray * t);
        return true;
    }

    private static Vector3d Unit(Vector3d v)
    {
        double length = v.Length;
        return length > 1e-9 ? v / length : new Vector3d(0, 0, 1);
    }

    private static Bgra32 Shade(Bgra32 colour, bool hot) =>
        hot ? Bgra32.FromRgb(255, 222, 90) : Bgra32.FromRgba(colour.R, colour.G, colour.B, 215);

    private static double Snapped(double value, double step) => step > 0 ? Math.Round(value / step) * step : value;

    private static Vector3d SnapEach(Vector3d v, double step) => new(Snapped(v.X, step), Snapped(v.Y, step), Snapped(v.Z, step));
}
