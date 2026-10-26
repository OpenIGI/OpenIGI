using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Computer;

namespace OpenIGI.Game.Igi1.Computer;

/// <summary>
/// The computer's map screen, <c>ComputerMap</c> (<c>0x469740</c>): a camera hung high over the level looking straight down,
/// panned by the arrows and the sphere, zoomed by plus, minus, the wheel or a double click, and moved to whatever is clicked.
/// </summary>
public sealed partial class ComputerMap : IComputerScreen
{
    /// <summary>The tangents of the four zoom levels' half angles come from these angles (<c>0x53F750</c>).</summary>
    public static readonly float[] ZoomAngles = [0.7853982f, 0.32724923f, 0.13962634f, 0.009599311f];

    /// <summary>How far the camera hangs above the point it looks at, world units (225 metres).</summary>
    public const float Back = 921600f;

    /// <summary>How far below itself the camera looks for the ground each tick.</summary>
    public const double Drop = 1843200.0;

    /// <summary>How far an arrow pans a tick at a field of view of one, world units.</summary>
    public const float Pan = 27306.666f;

    /// <summary>How far the sphere drags for a whole channel of mouse.</summary>
    public const float Drag = 409600f;

    /// <summary>The power of the field of view that scales a pan.</summary>
    public const float PanPower = 0.7f;

    /// <summary>The click's line, world units.</summary>
    public const double Far = 4096000.0;

    /// <summary>How near to a click's point a human, a mine or a lens is found, squared, at a field of view of one.</summary>
    public const double Near = 167772160.0;

    /// <summary>The ticks a second click has to come within to be a double click.</summary>
    public const int DoubleClick = 13;

    private readonly Igi1Computer _computer;
    private readonly ComputerMapNoise<X87> _noise = new();
    private int _clickTime;
    private int _rightClickTime;
    private float _clickX;
    private float _clickY;
    private int _lastButtons;

    /// <summary>Initializes a new instance of the <see cref="ComputerMap"/> class, as its create handler does.</summary>
    /// <param name="computer">The computer.</param>
    public ComputerMap(Igi1Computer computer)
    {
        _computer = computer ?? throw new ArgumentNullException(nameof(computer));
        IIgi1ComputerWorld world = computer.World;
        if (computer.MapStartTask is { } start)
        {
            LookAtTask(start);
            computer.MapStartTask = null;
        }

        FieldOfView = Tan(ZoomAngles[computer.Zoom]);
        CameraPosition = computer.MapPosition;
        CameraFieldOfView = FieldOfView;
        _noise.Reset();

        float w = computer.Width, h = computer.Height;
        float x = w * 0.834375f, y = h * 0.7125f;
        Button(3, x - 23f, y - 47f, "arrow1");
        Button(4, x - 23f, y + 18f, "arrow3");
        Button(5, x - 47f, y - 23f, "arrow4");
        Button(6, x + 18f, y - 23f, "arrow2");
        Button(7, x - 48f, y + 48f, "plus");
        Button(8, x + 24f, y + 48f, "minus");
        Button(9, x - 17f, y - 17f, "sphere");
        float tabX = w * 0.1f;
        computer.AddTab(1, tabX, h * 0.80833334f, "OBJECTIVES", OnTab);
        computer.AddTab(2, tabX, h * 0.775f, "LOG", OnTab);

        Startup = new ComputerStartup(
            ["INITIALIZING", "CONNECTING", "DOWNLOADING"],
            [9f, 4.5f, 9f],
            (0.1f, 1f, 0.1f),
            (0f, 0.5f, 0.1f));
        Startup.SetCallback(0, OnStatus);
        Startup.SetCallback(1, OnStatus);
        Startup.SetCallback(2, OnStatus);
        Startup.Start();
    }

    /// <summary>Which of the level's objects the map shows, <c>ComputerObject.flags</c>.</summary>
    [Flags]
    public enum Layers
    {
        /// <summary>Nothing, while the boot box is up.</summary>
        None = 0,

        /// <summary>The humans and the player.</summary>
        Humans = 0x1,

        /// <summary>The helicopters and cars.</summary>
        Vehicles = 0x2,

        /// <summary>The highlights.</summary>
        Hilights = 0x4,

        /// <summary>The cameras, from the first close zoom.</summary>
        Cameras = 0x8,

        /// <summary>The mines and the stationary guns.</summary>
        Objects = 0x10,
    }

    /// <summary>Gets the tangent of the camera's half angle, <c>fov</c>.</summary>
    public float FieldOfView { get; private set; }

    /// <summary>Gets where the camera stood as the last tick ended, which the picture is drawn from and a click is cast from.</summary>
    public Vector3d CameraPosition { get; private set; }

    /// <summary>Gets the camera's field of view as the last tick ended.</summary>
    public float CameraFieldOfView { get; private set; }

    /// <summary>Gets the interference's level, the line filter's.</summary>
    public float Noise => _noise.Level;

    /// <summary>Gets the boot box.</summary>
    public ComputerStartup Startup { get; }

    /// <summary>Gets which objects the map shows.</summary>
    public Layers Shown { get; private set; }

    /// <summary>Gets the task the map follows, or <see langword="null"/>.</summary>
    public GameTask? Target { get; private set; }

    /// <summary>Gets the camera whose view the map shows, the last lens clicked, or <see langword="null"/>.</summary>
    public GameTask? SelectedLens { get; private set; }

    /// <inheritdoc/>
    public ComputerScreenKind Kind => ComputerScreenKind.Map;

    /// <inheritdoc/>
    public void Run()
    {
        _computer.Panel.Run();
        Startup.Run();
        Trace();
        DropCamera();
        ClampCamera();
        CameraPosition = _computer.MapPosition;
        CameraFieldOfView = FieldOfView;
        _noise.Run(_computer.World.Random);
        BuildMarks();
    }

    /// <summary>Looks at a point: the camera hangs <see cref="Back"/> above it (<c>0x469D00</c>).</summary>
    /// <param name="at">The point.</param>
    public void LookAt(Vector3d at)
    {
        // The tilt's second column is (0, 0, -1) to the float's precision; its x and y terms are what the rotation leaves.
        (float x, float y, float z) = TiltColumn;
        _computer.MapPosition = new Vector3d(
            ((X87)x * -Back).Value + at.X,
            ((X87)y * -Back).F + at.Y,
            ((X87)z * -Back).F + at.Z);
    }

    // RotX(-π/2)'s second column, (0, cos, sin), as Matrix_SetRotX leaves it in floats.
    private static readonly (float X, float Y, float Z) TiltColumn = (0f, (float)Math.Cos(-1.5707964f), (float)Math.Sin(-1.5707964f));

    /// <summary>
    /// Gets the camera's axes, by rows: the tilt turned a further quarter about X (<c>0x4B34F0</c>), so its columns are the
    /// screen's right (east), the screen's down (south) and the view (down), to the floats' precision.
    /// </summary>
    public static Matrix3f View { get; } = ViewAxes();

    private static Matrix3f ViewAxes()
    {
        float c = TiltColumn.Y, s = TiltColumn.Z;

        // RotX(a) by rows is (1, 0, 0), (0, c, -s), (0, s, c); the view is RotX(a) times RotX(a).
        float c2 = (c * c) + (-s * s), cs = (c * -s) + (-s * c), sc = (s * c) + (c * s);
        return Matrix3f.FromRows(new Vector3f(1f, 0f, 0f), new Vector3f(0f, c2, cs), new Vector3f(0f, sc, c2));
    }

    // The x87's tangent of a float angle, stored to a float.
    private static float Tan(float angle) => (float)Math.Tan(angle);

    private void Button(int id, float x, float y, string sprite)
    {
        ComputerSprites sprites = new($"{sprite}_1.spr", $"{sprite}_2.spr", $"{sprite}_3.spr");
        _computer.Panel.Add(new ComputerButtonItem(id, x, y, sprites, _computer.World.SpriteSize(sprites.Normal), OnButton));
    }

    // 0x469660: as each line of the boot box ends, more of the map comes up.
    private void OnStatus(int step)
    {
        switch (step)
        {
            case 0:
                Shown = (Layers)4;
                Shown = (Layers)0x15;
                break;
            case 1:
                Shown = (Layers)0x15;
                break;
            case 2:
                Shown = (Layers)0x1F;
                break;
        }
    }

    // 0x469D70.
    private void OnTab(ComputerItem item, int id)
    {
        if ((_computer.Pressed & Igi1Computer.Left) == 0)
        {
            return;
        }

        if (id == 1)
        {
            _computer.SetScreen(ComputerScreenKind.Objectives);
        }
        else if (id == 2)
        {
            _computer.SetScreen(ComputerScreenKind.Log);
        }
    }

    // 0x469DA0: the arrows pan while held, plus and minus zoom on the press, and the sphere drags the map under the mouse.
    private void OnButton(ComputerItem item, int id)
    {
        if (item.Id != id || item is not ComputerButtonItem button)
        {
            return;
        }

        bool down = (_computer.Buttons & Igi1Computer.Left) != 0;
        button.WasDown = button.IsDown;
        button.IsDown = down;
        float dx = 0f, dy = 0f;
        if (down)
        {
            switch (id)
            {
                case 3:
                    ClearTarget();
                    dy = Pan;
                    break;
                case 4:
                    ClearTarget();
                    dy = -Pan;
                    break;
                case 5:
                    ClearTarget();
                    dx = -Pan;
                    break;
                case 6:
                    ClearTarget();
                    dx = Pan;
                    break;
                case 7 when !button.WasDown:
                    if (_computer.Zoom < 3)
                    {
                        _computer.Zoom++;
                        Rezoomed();
                    }

                    _computer.World.PlaySound("mapcomp_01");
                    break;
                case 8 when !button.WasDown:
                    if (_computer.Zoom != 0)
                    {
                        _computer.Zoom--;
                        Rezoomed();
                    }

                    _computer.World.PlaySound("mapcomp_01");
                    break;
            }
        }

        SetFieldOfView(Tan(ZoomAngles[_computer.Zoom]));
        if (id == 9)
        {
            if (down)
            {
                // The vertical channel arrives with the profile's inversion; the drag undoes it the other way about.
                float mx = _computer.Input.LookX, my = _computer.Input.LookY;
                if (!_computer.Input.InvertMouse)
                {
                    my = -my;
                }

                _computer.IsDragging = true;
                dx = mx * Drag;
                dy = my * Drag;
                ClearTarget();
            }
            else
            {
                _computer.IsDragging = false;
            }
        }

        X87 f = Math.Pow(FieldOfView, PanPower);
        Vector3d at = _computer.MapPosition;
        _computer.MapPosition = new Vector3d((f * dx).F + at.X, (f * dy).Value + at.Y, at.Z);
    }

    private void Rezoomed()
    {
        Shown = Layers.None;
        Startup.Start();
    }

    // 0x469FE0: a new field of view flares the interference and whirs.
    private void SetFieldOfView(float fov)
    {
        if (fov == FieldOfView)
        {
            return;
        }

        FieldOfView = fov;
        _noise.Flare(_computer.World.Random);
        _computer.World.PlaySound("mapcomp_05");
        _computer.World.PlaySound("mapcomp_03");
    }

    private void ClearTarget() => Target = null;

    // 0x469CC0.
    private void LookAtTask(GameTask task)
    {
        if (_computer.World.TryLocate(task, out Vector3d at))
        {
            LookAt(at);
        }
    }

    // 0x46A2D0: the camera keeps its height over whatever is under it.
    private void DropCamera()
    {
        if (_computer.World.TryDrop(_computer.MapPosition, Drop, out Vector3d ground))
        {
            LookAt(ground);
        }
    }

    // 0x46A200.
    private void ClampCamera()
    {
        if (_computer.Limit is { } limit)
        {
            _computer.MapPosition = limit.Clamp<X87>(_computer.MapPosition);
        }
    }

    // 0x46A9F0: only what answers that it may be followed is.
    private void SetTarget(GameTask task)
    {
        ClearTarget();
        if (_computer.World.CanFollow(task))
        {
            Target = task;
        }
    }
}
