using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// A draggable, on-screen rotation gizmo for the sun, for the free camera.
/// </summary>
/// <remarks>
/// <para>
/// Three world-axis rings float in front of the camera with a stub pointing at the current sun; dragging a
/// ring turns the sun about that axis, exactly as the numpad keys of the <see cref="DayNightCycle"/> do,
/// but with the mouse. It is a screen-space gizmo: the rings are circles in world space, but they are
/// projected to the screen through the same camera transform the frame is drawn with and then drawn and
/// picked as flat polylines. That avoids a world-space line pipeline the renderer does not otherwise have,
/// and picking a ring becomes a plain distance-to-polyline test against the cursor.
/// </para>
/// <para>
/// The drag maps the change in the cursor's angle about the gizmo's centre onto the ring's axis -- the
/// motion any rotation gizmo answers to. It reads a freed cursor's absolute position, so the caller frees
/// the cursor while the gizmo is up, the way the console does for its own pointer.
/// </para>
/// </remarks>
internal sealed class SunGizmo
{
    // How far in front of the camera the gizmo floats, and how wide its rings are, in metres.
    private const float DistanceMetres = 6f;
    private const float RadiusMetres = 1.6f;

    // Points around each ring, and how near the cursor must fall to a ring to grab it.
    private const int Segments = 48;
    private const float PickPixels = 14f;

    private readonly ScreenPoint[][] _rings =
    [
        new ScreenPoint[Segments + 1],
        new ScreenPoint[Segments + 1],
        new ScreenPoint[Segments + 1],
    ];

    private ScreenPoint _centre;
    private ScreenPoint _sunTip;
    private bool _haveGeometry;

    private int _dragAxis = -1;
    private int _hoverAxis = -1;
    private float _dragAngle;

    /// <summary>Gets whether the gizmo is up and taking the cursor.</summary>
    public bool Active { get; private set; }

    /// <summary>Shows or hides the gizmo. Hiding it drops any drag in progress.</summary>
    public void Toggle()
    {
        Active = !Active;
        if (!Active)
        {
            _dragAxis = -1;
            _hoverAxis = -1;
            _haveGeometry = false;
        }
    }

    /// <summary>Drops the gizmo, for leaving the free camera.</summary>
    public void Hide()
    {
        Active = false;
        _dragAxis = -1;
        _hoverAxis = -1;
        _haveGeometry = false;
    }

    /// <summary>Projects the rings for this frame and turns a drag into sun rotation.</summary>
    /// <param name="eye">The camera the frame is drawn with.</param>
    /// <param name="unproject">The projection's scales, for the world-to-screen mapping.</param>
    /// <param name="sunDirection">The current sun direction, for the stub the gizmo points along.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <param name="mouseX">The cursor's client X, valid because the caller has freed the cursor.</param>
    /// <param name="mouseY">The cursor's client Y.</param>
    /// <param name="mouseDown">Whether the left button is held.</param>
    /// <param name="rotate">Applied one axis (0/1/2) and an angle when a ring is dragged.</param>
    public void Update(
        Camera3D eye,
        in DepthUnprojection unproject,
        Vector3f sunDirection,
        int width,
        int height,
        float mouseX,
        float mouseY,
        bool mouseDown,
        Action<int, float> rotate)
    {
        ArgumentNullException.ThrowIfNull(rotate);

        if (!Active)
        {
            _haveGeometry = false;
            _dragAxis = -1;
            return;
        }

        Vector3f forward = eye.Orientation.Row3;
        Vector3f centreRelative = forward * DistanceMetres;
        _centre = Project(eye, unproject, width, height, centreRelative);

        // The plane each ring lies in: the two world axes that are not its own. X spins in Y-Z, and so on.
        ReadOnlySpan<Vector3f> axisU = [Vector3f.UnitY, Vector3f.UnitX, Vector3f.UnitX];
        ReadOnlySpan<Vector3f> axisV = [Vector3f.UnitZ, Vector3f.UnitZ, Vector3f.UnitY];

        for (int axis = 0; axis < 3; axis++)
        {
            for (int step = 0; step <= Segments; step++)
            {
                float t = (step / (float)Segments) * MathF.PI * 2f;
                Vector3f point = centreRelative
                    + (axisU[axis] * (MathF.Cos(t) * RadiusMetres))
                    + (axisV[axis] * (MathF.Sin(t) * RadiusMetres));
                _rings[axis][step] = Project(eye, unproject, width, height, point);
            }
        }

        _sunTip = Project(eye, unproject, width, height, centreRelative + (sunDirection * (RadiusMetres * 1.35f)));
        _haveGeometry = true;

        if (!mouseDown)
        {
            _dragAxis = -1;
        }

        if (_dragAxis < 0)
        {
            _hoverAxis = NearestRing(mouseX, mouseY);

            if (mouseDown && _hoverAxis >= 0)
            {
                _dragAxis = _hoverAxis;
                _dragAngle = AngleAboutCentre(mouseX, mouseY);
            }
        }
        else
        {
            float now = AngleAboutCentre(mouseX, mouseY);
            float delta = WrapPi(now - _dragAngle);
            _dragAngle = now;
            rotate(_dragAxis, delta);
        }
    }

    /// <summary>Draws the rings and the sun stub into the overlay batch.</summary>
    /// <param name="batch">The overlay sprite batch, already begun.</param>
    public void Draw(SpriteBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (!Active || !_haveGeometry)
        {
            return;
        }

        ReadOnlySpan<Bgra32> baseColours =
        [
            new Bgra32(230, 70, 70, 210),   // X, red
            new Bgra32(90, 210, 90, 210),   // Y, green
            new Bgra32(90, 140, 235, 210),  // Z, blue
        ];

        for (int axis = 0; axis < 3; axis++)
        {
            bool lit = axis == _dragAxis || (axis == _hoverAxis && _dragAxis < 0);
            Bgra32 colour = lit ? new Bgra32(255, 235, 120, 240) : baseColours[axis];
            float thickness = lit ? 2.4f : 1.5f;

            ScreenPoint[] ring = _rings[axis];
            for (int step = 0; step < Segments; step++)
            {
                ScreenPoint a = ring[step];
                ScreenPoint b = ring[step + 1];
                if (a.Visible && b.Visible)
                {
                    Line(batch, a.X, a.Y, b.X, b.Y, thickness, colour);
                }
            }
        }

        // The stub to the sun, and a small square at its tip, so the gizmo shows where the sun sits.
        if (_centre.Visible && _sunTip.Visible)
        {
            Bgra32 sun = new(255, 220, 90, 240);
            Line(batch, _centre.X, _centre.Y, _sunTip.X, _sunTip.Y, 2f, sun);
            Quad(batch, _sunTip.X - 4f, _sunTip.Y - 4f, _sunTip.X + 4f, _sunTip.Y + 4f, sun);
        }
    }

    private int NearestRing(float mouseX, float mouseY)
    {
        int best = -1;
        float bestDistance = PickPixels;

        for (int axis = 0; axis < 3; axis++)
        {
            ScreenPoint[] ring = _rings[axis];
            for (int step = 0; step < Segments; step++)
            {
                ScreenPoint a = ring[step];
                ScreenPoint b = ring[step + 1];
                if (!a.Visible || !b.Visible)
                {
                    continue;
                }

                float distance = DistanceToSegment(mouseX, mouseY, a.X, a.Y, b.X, b.Y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = axis;
                }
            }
        }

        return best;
    }

    private float AngleAboutCentre(float x, float y) => MathF.Atan2(y - _centre.Y, x - _centre.X);

    private static float WrapPi(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.PI * 2f;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.PI * 2f;
        }

        return angle;
    }

    // World point (relative to the camera) to a client-pixel position, through the same transform the frame
    // is drawn with: rotate into view by the camera's inverse orientation, divide by depth against the
    // projection's own scales, then to pixels with Y flipped for the top-left origin.
    private static ScreenPoint Project(
        Camera3D eye, in DepthUnprojection unproject, int width, int height, Vector3f relative)
    {
        Vector3f view = eye.Orientation.Transposed().Transform(relative);
        if (view.Z <= 0.05f)
        {
            return new ScreenPoint(0f, 0f, false);
        }

        float ndcX = (view.X * unproject.XScale) / view.Z;
        float ndcY = (view.Y * unproject.YScale) / view.Z;
        float x = ((ndcX * 0.5f) + 0.5f) * width;
        float y = (1f - ((ndcY * 0.5f) + 0.5f)) * height;
        return new ScreenPoint(x, y, true);
    }

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared < 1e-4f)
        {
            float ox0 = px - ax;
            float oy0 = py - ay;
            return MathF.Sqrt((ox0 * ox0) + (oy0 * oy0));
        }

        float t = Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lengthSquared, 0f, 1f);
        float cx = ax + (t * dx);
        float cy = ay + (t * dy);
        float ox = px - cx;
        float oy = py - cy;
        return MathF.Sqrt((ox * ox) + (oy * oy));
    }

    // A line as a thin quad: the segment offset each way by half the thickness along its own normal.
    private static void Line(SpriteBatch batch, float x0, float y0, float x1, float y1, float thickness, Bgra32 colour)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-3f)
        {
            return;
        }

        float nx = (-dy / length) * (thickness * 0.5f);
        float ny = (dx / length) * (thickness * 0.5f);

        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(x0 + nx, y0 + ny, 0f, 0f, colour),
            new Vertex2D(x1 + nx, y1 + ny, 1f, 0f, colour),
            new Vertex2D(x1 - nx, y1 - ny, 1f, 1f, colour),
            new Vertex2D(x0 - nx, y0 - ny, 0f, 1f, colour));
    }

    private static void Quad(SpriteBatch batch, float x0, float y0, float x1, float y1, Bgra32 colour)
    {
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(x0, y0, 0f, 0f, colour),
            new Vertex2D(x1, y0, 1f, 0f, colour),
            new Vertex2D(x1, y1, 1f, 1f, colour),
            new Vertex2D(x0, y1, 0f, 1f, colour));
    }

    private readonly record struct ScreenPoint(float X, float Y, bool Visible);
}
