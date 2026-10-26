using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>What the map asks of the level: the ground under it, what a line from it meets, and what it may follow.</summary>
public interface IIgi2ComputerMapWorld
{
    /// <summary>The ground under a point, <c>World_GetNextZ</c> over the terrain's group.</summary>
    /// <param name="from">The point.</param>
    /// <param name="distance">How far down to look, negative.</param>
    /// <returns>Where the line met the ground, or <see langword="null"/>.</returns>
    Vector3d? GroundBelow(Vector3d from, double distance);

    /// <summary>A line through everything, <c>World_TraceLines</c>.</summary>
    /// <param name="start">Where it starts.</param>
    /// <param name="finish">Where it ends.</param>
    /// <returns>Where it stopped, and what it hit there if it hit something that can be followed.</returns>
    (Vector3d Position, bool IsHit, IIgi2CameraTarget? Followable) Trace(Vector3d start, Vector3d finish);

    /// <summary>A task of the level by id, as something the map can look at.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The task, or <see langword="null"/> when there is none or it cannot say where it is.</returns>
    IIgi2CameraTarget? Task(int id);

    /// <summary>The level's humans: where they stand, and as something the map may follow when it may follow them.</summary>
    IEnumerable<(Vector3d Position, IIgi2CameraTarget? Followable)> Humans { get; }

    /// <summary>Sets off the first remote mine near a point, the single-player game's click on the map.</summary>
    /// <param name="at">The point.</param>
    /// <param name="reachSquared">How near, squared.</param>
    /// <returns>Whether one was set off.</returns>
    bool DetonateRemoteMine(Vector3d at, double reachSquared);
}

/// <summary>
/// The computer's map, <c>ComputerMap</c> (<c>computermap.c</c>): a camera high over the level looking straight down, north up,
/// held a fixed height over the ground under it and inside the level's limit; zoomed by four steps, panned by Ctrl and the
/// arrows or by dragging, re-centred by a double click, and able to follow a friend or a vehicle clicked on.
/// </summary>
/// <remarks>
/// Where it looks, its zoom and where it last looked outlive the screen (<c>ComputerMap_tPos</c>, <c>ComputerMap_nZoomLevel</c>,
/// <c>ComputerMap_tLastPos</c>), as their globals do. Its noise and its zoom's noise draw from the run stream.
/// </remarks>
public sealed class Igi2ComputerMap
{
    /// <summary>How far above what it looks at the camera stands, along its view, <c>921600</c>.</summary>
    public const double Distance = 921600.0;

    /// <summary>How far down the ground is looked for under the camera, <c>-1843200</c>.</summary>
    public const double GroundReach = -1843200.0;

    /// <summary>How far a line from the camera reaches, <c>4096000</c>.</summary>
    public const double TraceReach = 4096000.0;

    /// <summary>How far a Ctrl and arrow step moves the camera at the widest zoom, <c>27306.666</c>.</summary>
    public const float ScrollStep = 27306.666015625f;

    /// <summary>How far a drag across the whole screen moves it, <c>819200</c>.</summary>
    public const float DragScale = 819200f;

    /// <summary>How long a second click may follow the first to count as a double click, in ticks.</summary>
    public const int DoubleClickTicks = 13;

    /// <summary>The camera's height factor while the map is shown, <c>vHeightFactor</c>.</summary>
    public const float HeightFactor = 4096f;

    /// <summary>The half angles of the four zooms, <c>ComputerMap_avZoomLevel</c>.</summary>
    public static IReadOnlyList<float> ZoomLevels { get; } = [0.785398f, 0.261799f, 0.109083f, 0.017453f];

    // Real32x9_RotateAlpha(identity alpha, -pi/2): the view's right, forward and up as columns, x87-rounded.
    private static readonly float Cos = (float)Math.Cos(-1.5707963705062866);

    /// <summary>Gets where the camera stands, <c>ComputerMap_tPos</c>.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets where it stood when the map was last left, <c>ComputerMap_tLastPos</c>.</summary>
    public Vector3d LastPosition { get; private set; }

    /// <summary>Gets the point it eases towards, nought for none, <c>ComputerMap_tTargetPos</c>.</summary>
    public Vector3d TargetPosition { get; private set; }

    /// <summary>Gets the ground point last under the pointer, which a drag holds the pointer on, <c>ComputerMap_tLockPos</c>.</summary>
    public Vector3d LockPosition { get; private set; }

    /// <summary>Gets or sets the zoom step, nought widest, <c>ComputerMap_nZoomLevel</c> (<c>Computer_nZoomLevel</c> to scripts).</summary>
    public int ZoomLevel { get; set; }

    /// <summary>Gets the tangent of the camera's half angle, which eases to the zoom step's, <c>vFOV</c>.</summary>
    public float FieldOfView { get; private set; } = 1f;

    /// <summary>Gets the picture's noise, <c>vNoise</c>.</summary>
    public float Noise => _noise.Level;

    /// <summary>Gets the ticks until the noise flares again, <c>nNoiseTimer</c>.</summary>
    public int NoiseTimer => _noise.Timer;

    // The interference, as both games run it.
    private readonly OpenIGI.Game.Computer.ComputerMapNoise<Fpu> _noise = new();

    /// <summary>Gets what the camera follows, or <see langword="null"/>, <c>ptFollowQTaskRef</c>.</summary>
    public IIgi2CameraTarget? Following { get; private set; }

    /// <summary>Gets or sets what the map looks at next time it is shown, <c>ComputerMap_ptPositionQTask</c>.</summary>
    public Func<IIgi2ComputerMapWorld?, Vector3d?>? PositionTask { get; set; }

    /// <summary>Sets the task the map looks at next time it is shown, by its level id.</summary>
    public int PositionTaskId
    {
        set => PositionTask = world => world?.Task(value)?.CameraTargetPosition;
    }

    /// <summary>Gets a value indicating whether the scroll sound runs, the map being moved.</summary>
    public bool IsScrolling { get; private set; }

    /// <summary>Gets the view's right, up and forward axes, as rows: east, north and straight down.</summary>
    public static Matrix3f Orientation { get; } = Matrix3f.FromRows(new Vector3f(1f, 0f, 0f), new Vector3f(0f, 1f, 0f), new Vector3f(0f, 0f, -1f));

    private bool _isEntering;
    private int _zoomInClick;
    private int _zoomOutClick;
    private bool _isMouseLeft;
    private float _traceX;
    private float _traceY;

    // The forward axis (0, cos, sin) of the base frame, sin being -1.
    private static Vector3d Back(Vector3d at) => new(
        at.X + (0.0 * -Distance),
        ((Fpu)Cos * -Distance).Value + at.Y,
        ((Fpu)(-1f) * -Distance).Value + at.Z);

    /// <summary>
    /// The screen is shown, <c>ComputerMap_CreateHandler</c>: the camera's field of view the zoom's, the noise steady for two
    /// seconds; it will look at the task it was sent to on its first run.
    /// </summary>
    /// <param name="display">The open computer.</param>
    public void Enter(Igi2ComputerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        _isEntering = true;
        FieldOfView = (float)Math.Tan(ZoomLevels[Math.Clamp(ZoomLevel, 0, 3)]);
        _noise.Reset();
        _zoomInClick = 0;
        _zoomOutClick = 0;
        Following = null;
        IsScrolling = false;
    }

    /// <summary>The screen is left, <c>ComputerMap_DeleteHandler</c>: where it looked is remembered.</summary>
    public void Leave()
    {
        LastPosition = Position;
        Following = null;
        IsScrolling = false;
    }

    /// <summary>
    /// One tick, <c>ComputerMap_RunHandler</c>: the input; the camera put back over the ground and inside the limit; the noise.
    /// </summary>
    /// <param name="display">The open computer.</param>
    /// <param name="input">The keys and the mouse.</param>
    /// <param name="world">The level, or <see langword="null"/>.</param>
    /// <param name="random">The run stream.</param>
    public void Run(Igi2ComputerDisplay display, in Igi2ComputerInput input, IIgi2ComputerMapWorld? world, IgiRandom? random)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (_isEntering)
        {
            _isEntering = false;
            if (PositionTask is { } task)
            {
                SetFromTask(task(world));
                PositionTask = null;
            }
        }

        _isMouseLeft = input.MouseLeft;
        Trace(display, input, world, random);
        if (world?.GroundBelow(Position, GroundReach) is { } ground)
        {
            Position = Back(ground);
        }

        if (display.Computer.Limit is { } limit)
        {
            Position = limit.Limit.Clamp<Fpu>(Position);
        }

        if (random is not null)
        {
            _noise.Run(random);
        }
        else if (NoiseTimer != 0)
        {
            _noise.Timer--;
            _noise.Level = ((Fpu)Noise - 0.03333333507180214).F;
        }
        else
        {
            _noise.Level = ((Fpu)Noise - 0.03333333507180214).F;
        }

        // ComputerMap_DrawHandler: while dragged, the pointer is put back on the ground point it took hold of.
        if (display.IsPointerLocked && Project(LockPosition, display.Width, display.Height) is { } held)
        {
            display.HoldPointer(held.X, held.Y);
        }
    }

    /// <summary>Where a world point falls on the map's screen, <c>TransContext_ProjectWorldPos2</c>, or null behind the camera.</summary>
    /// <param name="at">The point.</param>
    /// <param name="width">The screen's width.</param>
    /// <param name="height">Its height.</param>
    /// <returns>The point on the screen.</returns>
    public (float X, float Y)? Project(Vector3d at, float width, float height)
    {
        double dx = at.X - Position.X, dy = at.Y - Position.Y, depth = Position.Z - at.Z;
        if (depth <= 0.0 || FieldOfView <= 0f)
        {
            return null;
        }

        double across = dx / depth / FieldOfView, down = -dy / depth / FieldOfView;
        return ((float)((across * width * 0.5) + (width * 0.5)), (float)((down * width * 0.5) + (height * 0.5)));
    }

    // ComputerMap_SetCameraPosFromTask: over the task while the map has not looked anywhere yet or was sent to it; else where it
    // last looked.
    private void SetFromTask(Vector3d? at)
    {
        if (at is { } position && (LastPosition == default || PositionTask is not null))
        {
            Position = Back(position);
        }
        else
        {
            Position = Back(LastPosition);
        }
    }

    // ComputerMap_CheckCameraPosAgainstLimit.
    private static bool InsideLimit(Igi2DefineComputerLimitTask? limit, Vector3d at) => limit?.Limit.Contains<Fpu>(at) ?? true;

    // ComputerMap_PlayZoomSound: the noise flares and the zoom whirs, two draws from the run stream.
    private void PlayZoomSound(Igi2ComputerDisplay display, IgiRandom? random)
    {
        if (random is not null)
        {
            _noise.Flare(random);
        }

        display.PlaySound?.Invoke("mapcomp_03");
    }

    /// <summary>
    /// The map's input, <c>ComputerMap_Trace</c>: Ctrl and the arrows pan by a step; the left button held off the menu drags the map
    /// under a held pointer; a double click, or the wheel, zooms in on the point under the pointer and centres it; a right double
    /// click, or the wheel back, zooms out. The point clicked or zoomed on is found by a line from the camera through the pointer,
    /// and a friend or a vehicle there is followed.
    /// </summary>
    private void Trace(Igi2ComputerDisplay display, in Igi2ComputerInput input, IIgi2ComputerMapWorld? world, IgiRandom? random)
    {
        bool scroll = false, trace = false, centre = false, zoomIn = false, zoomOut = false;
        float stepX = 0f, stepY = 0f;
        if (input.Control)
        {
            if (input.Up) { Following = null; stepY = ScrollStep; scroll = true; }
            if (input.Down) { Following = null; scroll = true; stepY = ((Fpu)stepY - ScrollStep).F; }
            if (input.Left) { Following = null; stepX = -ScrollStep; scroll = true; }
            if (input.Right) { Following = null; scroll = true; stepX = ((Fpu)stepX + ScrollStep).F; }
        }

        float speed = (float)Math.Pow(FieldOfView, 0.699999988079071);
        Position = new Vector3d(
            ((Fpu)stepX * speed).Value + Position.X,
            ((Fpu)stepY * speed).F + Position.Y,
            ((Fpu)speed * 0.0).F + Position.Z);

        bool menuLit = display.Items.Active is not null;
        if (!input.MouseLeft || menuLit)
        {
            display.IsPointerLocked = false;
        }
        else
        {
            scroll = true;
            if (!display.IsPointerLocked)
            {
                display.IsPointerLocked = true;
                _traceX = display.PointerX;
                _traceY = display.PointerY;
                trace = true;
            }

            float dragX = ((Fpu)(-display.MouseSpeedX) * DragScale).F, dragY = ((Fpu)display.MouseSpeedY * DragScale).F;
            float dragSpeed = (float)Math.Pow(FieldOfView, 0.699999988079071);
            Position = new Vector3d(
                ((Fpu)dragX * dragSpeed).Value + Position.X,
                ((Fpu)dragY * dragSpeed).F + Position.Y,
                ((Fpu)dragSpeed * 0.0).F + Position.Z);
            Following = null;
        }

        if ((display.EdgeButtons & Igi2ComputerButtons.Select) != 0 && display.Items.Active is null)
        {
            if (_zoomInClick != 0)
            {
                zoomIn = true;
                _zoomInClick = 0;
            }
            else
            {
                _zoomInClick = DoubleClickTicks;
            }
        }

        if (input.WheelUp)
        {
            zoomIn = true;
        }

        if ((display.EdgeButtons & Igi2ComputerButtons.Back) != 0 && display.Items.Active is null)
        {
            if (_zoomOutClick != 0)
            {
                zoomOut = true;
                _zoomOutClick = 0;
            }
            else
            {
                _zoomOutClick = DoubleClickTicks;
            }
        }

        if (input.WheelDown)
        {
            zoomOut = true;
        }

        if (input.Home && PositionTask is { } task)
        {
            LastPosition = default;
            SetFromTask(task(world));
        }

        if (Following is { } followed)
        {
            TargetPosition = followed.CameraTargetPosition;
        }

        if (TargetPosition != default)
        {
            double dx = TargetPosition.X - Position.X, dy = TargetPosition.Y - Position.Y;
            if (Math.Abs(dx) > 409.6000061035156 || Math.Abs(dy) > 409.6000061035156)
            {
                Position = new Vector3d((dx * 0.3333333432674408) + Position.X, (dy * 0.3333333432674408) + Position.Y, Position.Z);
            }
            else
            {
                TargetPosition = default;
            }
        }

        if (zoomIn)
        {
            _traceX = display.PointerX;
            _traceY = display.PointerY;
            trace = true;
            centre = true;
            display.CentrePointer();
            if (ZoomLevel < 3)
            {
                ZoomLevel++;
                PlayZoomSound(display, random);
            }
        }

        if (zoomOut && ZoomLevel != 0)
        {
            ZoomLevel--;
            PlayZoomSound(display, random);
            display.CentrePointer();
        }

        float ideal = (float)Math.Tan(ZoomLevels[ZoomLevel]);
        float eased = (((Fpu)ideal - FieldOfView) * 0.3333333432674408 + FieldOfView).F;
        if (eased != FieldOfView)
        {
            FieldOfView = eased;
        }

        if (_zoomInClick > 0)
        {
            _zoomInClick--;
        }

        if (_zoomOutClick > 0)
        {
            _zoomOutClick--;
        }

        if (trace && world is not null)
        {
            TraceFromPointer(display, world, centre);
        }

        if (scroll && !IsScrolling)
        {
            display.PlaySound?.Invoke("mapcomp_04");
        }

        IsScrolling = scroll;
    }

    // The line through the pointer where the trace began: the ground point it meets is held under a drag and centred by a zoom;
    // a friend near it, or what it hit, is followed.
    private void TraceFromPointer(Igi2ComputerDisplay display, IIgi2ComputerMapWorld world, bool centre)
    {
        float width = display.Width, height = display.Height;
        if (_traceX < 0f || _traceY < 0f || _traceX >= width || _traceY >= height)
        {
            return;
        }

        Vector3d direction = Ray(_traceX, _traceY, width, height, FieldOfView);
        Vector3d end = new(
            Position.X + (direction.X * TraceReach),
            Position.Y + (direction.Y * TraceReach),
            Position.Z + (direction.Z * TraceReach));
        (Vector3d hit, bool isHit, IIgi2CameraTarget? hitObject) = world.Trace(Position, end);
        LockPosition = hit;
        double reach = ((Fpu)FieldOfView * 167772160.0).Value;
        IIgi2CameraTarget? friend = null;
        bool near = false;
        foreach ((Vector3d at, IIgi2CameraTarget? followable) in world.Humans)
        {
            double dx = at.X - hit.X, dy = at.Y - hit.Y, dz = at.Z - hit.Z;
            if ((dz * dz) + (dy * dy) + (dx * dx) < reach)
            {
                friend = followable;
                near = true;
                break;
            }
        }

        // The single-player game: a click by a remote mine sets it off.
        bool picked = _isMouseLeft && world.DetonateRemoteMine(hit, reach);
        if (!isHit)
        {
            Following = null;
            return;
        }

        if (!picked && centre && InsideLimit(display.Computer.Limit, hit))
        {
            TargetPosition = hit;
        }

        if (near && centre)
        {
            Following = friend;
        }
        else
        {
            Following = hitObject;
        }
    }

    /// <summary>The direction from the camera through a point of the screen, the view's frame turned to the world's.</summary>
    /// <param name="x">The point's X, in the screen's pixels.</param>
    /// <param name="y">Its Y.</param>
    /// <param name="width">The screen's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="fieldOfView">The tangent of the view's half angle across.</param>
    /// <returns>The unit direction.</returns>
    public static Vector3d Ray(float x, float y, float width, float height, float fieldOfView)
    {
        double across = ((x + x) - (double)width) * fieldOfView / width;
        double down = ((y + y) - (double)height) * fieldOfView / width;

        // Right is east, down the screen is south, forward is straight down.
        double dx = across, dy = -down, dz = -1.0;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        return new Vector3d(dx / length, dy / length, dz / length);
    }
}
