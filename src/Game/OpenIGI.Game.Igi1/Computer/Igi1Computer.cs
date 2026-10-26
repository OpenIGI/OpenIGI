using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Computer;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Computer;

/// <summary>The computer's screens, as <c>0x46AFB0</c> numbers them.</summary>
public enum ComputerScreenKind
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>The map.</summary>
    Map = 1,

    /// <summary>The objectives.</summary>
    Objectives = 2,

    /// <summary>The log of messages.</summary>
    Log = 3,
}

/// <summary>One of the computer's screens.</summary>
public interface IComputerScreen
{
    /// <summary>Gets which screen it is.</summary>
    ComputerScreenKind Kind { get; }

    /// <summary>Runs it a tick, its controls first.</summary>
    void Run();
}

/// <summary>What the computer reads from the player's tick.</summary>
/// <param name="LookX">The look channel across: the mouse's counts over 255, times the profile's sensitivity.</param>
/// <param name="LookY">The look channel down, inverted when the profile inverts the mouse.</param>
/// <param name="InvertMouse">Whether the profile inverts the mouse, which the menu undoes.</param>
/// <param name="Buttons">The mouse's buttons: <see cref="Igi1Computer.Left"/>, <see cref="Igi1Computer.Right"/> and the wheel's bits.</param>
public readonly record struct Igi1ComputerInput(float LookX, float LookY, bool InvertMouse, int Buttons);

/// <summary>
/// Project I.G.I.'s computer, the <c>Computer</c> task and the <c>ComputerMenu</c> over it (<c>0x46BB10</c>, <c>0x46ABE0</c>):
/// opened from the weapon's idle state by the map key, it puts the device in the player's hands and a pointer over its
/// screens, and it closes on the key again or on the player's death. The game runs on behind it.
/// </summary>
public sealed class Igi1Computer
{
    /// <summary>The left button, as the menu keeps it.</summary>
    public const int Left = 0x10;

    /// <summary>The right button.</summary>
    public const int Right = 0x20;

    /// <summary>The left button as the mouse reports it.</summary>
    public const int MouseLeft = 0x1;

    /// <summary>The right button as the mouse reports it.</summary>
    public const int MouseRight = 0x2;

    /// <summary>The wheel turned away, one notch.</summary>
    public const int WheelUp = 0x100;

    /// <summary>The wheel turned back.</summary>
    public const int WheelDown = 0x200;

    /// <summary>How far ahead of the eye the device is held, world units.</summary>
    public const float DeviceForward = 532.48f;

    /// <summary>How far below the eye.</summary>
    public const float DeviceDown = 40.96f;

    private ComputerScreenKind _nextScreen;

    /// <summary>Initializes a new instance of the <see cref="Igi1Computer"/> class.</summary>
    /// <param name="world">The level it runs in.</param>
    public Igi1Computer(IIgi1ComputerWorld world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>Gets the level it runs in.</summary>
    public IIgi1ComputerWorld World { get; }

    /// <summary>Gets or sets the objectives it lists, the level's own.</summary>
    public MissionObjectiveRuntime? Objectives { get; set; }

    /// <summary>Gets or sets the square the map may look over, the level's <c>DefineComputerLimit</c>, or none.</summary>
    public ComputerMapLimit? Limit { get; set; }

    /// <summary>Gets a value indicating whether it is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether the level lets the player read its map (<c>0x46C0B0</c>): its player input's
    /// expression, each tick, or on when it has none.
    /// </summary>
    public bool MapEnabled { get; set; } = true;

    /// <summary>Gets a value indicating whether the device is drawn: only with the map, as the model is given only then.</summary>
    public bool ShowsDevice { get; private set; }

    /// <summary>Gets the display's width the screens are laid out across, pixels.</summary>
    public float Width { get; private set; } = 640f;

    /// <summary>Gets the display's height.</summary>
    public float Height { get; private set; } = 480f;

    /// <summary>
    /// Gets or sets the height of a display pixel over its width, which a click on the map is cast with (<c>0x491D40</c>).
    /// </summary>
    /// <remarks>
    /// The original takes a pixel to be <c>4h / 3w</c> of its width, as a 4:3 monitor shows any mode; a display today has square
    /// pixels, so it is one, which is what the original works out for every 4:3 mode.
    /// </remarks>
    public float PixelAspect { get; set; } = 1f;

    /// <summary>Gets the pointer across, display pixels.</summary>
    public float PointerX { get; private set; }

    /// <summary>Gets the pointer down.</summary>
    public float PointerY { get; private set; }

    /// <summary>Gets or sets a value indicating whether the sphere is dragging the map, which hides and holds the pointer.</summary>
    public bool IsDragging { get; set; }

    /// <summary>Gets the buttons held this tick, <see cref="Left"/> and <see cref="Right"/>.</summary>
    public int Buttons { get; private set; }

    /// <summary>Gets the buttons that went down this tick.</summary>
    public int Pressed { get; private set; }

    /// <summary>Gets this tick's input.</summary>
    public Igi1ComputerInput Input { get; private set; }

    /// <summary>Gets the controls of the screen showing.</summary>
    public ComputerPanel Panel { get; private set; } = new();

    /// <summary>Gets the screen showing, or <see langword="null"/> while closed.</summary>
    public IComputerScreen? Screen { get; private set; }

    /// <summary>Gets or sets the map's zoom, <c>Computer_nZoomLevel</c>, from nought (widest) to three; put back to nought on each opening.</summary>
    public int Zoom { get; set; }

    /// <summary>Gets or sets where the map's camera is, world units: kept from one showing of the map to the next.</summary>
    public Vector3d MapPosition { get; set; }

    /// <summary>Gets or sets the task the next map is opened over, or <see langword="null"/> to open it where it was.</summary>
    public GameTask? MapStartTask { get; set; }

    /// <summary>Opens it (<c>0x46BB10</c>): the device only with the map, the zoom widest, the map over the player.</summary>
    /// <param name="player">The player's task, which the map opens over.</param>
    /// <param name="width">The display's width the screens are laid out across.</param>
    /// <param name="height">Its height.</param>
    public void Open(GameTask? player, float width, float height)
    {
        Width = width;
        Height = height;
        IsOpen = true;
        ShowsDevice = MapEnabled;
        Zoom = 0;
        MapStartTask = player;

        // 0x46ABE0: the pointer starts in the middle of the display.
        PointerX = width * 0.5f;
        PointerY = height * 0.5f;
        IsDragging = false;
        Buttons = 0;
        Pressed = 0;
        _nextScreen = ComputerScreenKind.None;
        OpenScreen(MapEnabled ? ComputerScreenKind.Map : ComputerScreenKind.Objectives);
    }

    /// <summary>Closes it (<c>0x46BF80</c>).</summary>
    public void Close()
    {
        IsOpen = false;
        Screen = null;
        Panel = new ComputerPanel();
        IsDragging = false;
    }

    /// <summary>Asks for another screen, which comes up at the start of the next tick (<c>0x46B4E0</c>).</summary>
    /// <param name="screen">The screen.</param>
    public void SetScreen(ComputerScreenKind screen) => _nextScreen = screen;

    /// <summary>Runs the menu a tick (<c>0x46AC30</c>): the pointer moves with the mouse, the buttons are read, the screen runs.</summary>
    /// <param name="input">This tick's input.</param>
    /// <param name="paused">Whether the game is paused, when the menu does nothing.</param>
    public void Run(in Igi1ComputerInput input, bool paused)
    {
        if (!IsOpen || paused)
        {
            return;
        }

        Input = input;
        if (_nextScreen != ComputerScreenKind.None)
        {
            OpenScreen(_nextScreen);
            _nextScreen = ComputerScreenKind.None;
        }

        float w = Width, h = Height;
        if (!IsDragging)
        {
            // The vertical channel comes inverted when the profile inverts the mouse; the pointer undoes it.
            X87 my = input.LookY;
            if (input.InvertMouse)
            {
                my *= -1f;
            }

            float dx = ((X87)input.LookX * w * 0.5f).F;
            float x = ((X87)dx + PointerX).F;
            float y = ((my * h * 0.5f) + PointerY).F;
            PointerX = x;
            PointerY = y;
            X87 lo = (X87)w * 0.04843750f;
            if (lo.Value > x)
            {
                PointerX = lo.F;
            }
            else
            {
                X87 hi = (X87)w - ((X87)w * 0.04531250f);
                PointerX = !(hi.Value >= x) ? hi.F : x;
            }

            lo = (X87)h * 0.05416667f;
            if (lo.Value > y)
            {
                PointerY = lo.F;
            }
            else
            {
                X87 hi = (X87)h - ((X87)h * 0.09583334f);
                PointerY = !(hi.Value >= y) ? hi.F : y;
            }
        }

        int buttons = 0;
        if ((input.Buttons & MouseLeft) != 0)
        {
            buttons = Left;
        }

        if ((input.Buttons & MouseRight) != 0)
        {
            buttons |= Right;
        }

        Panel.SetPointer(RoundInt(PointerX), RoundInt(PointerY));
        int old = Buttons;
        Buttons = buttons;
        Pressed = ~old & buttons;
        Screen?.Run();
    }

    /// <summary>Adds one of a screen's text links (<c>0x46B970</c>), in font 3 and the link colour.</summary>
    /// <param name="id">Its id.</param>
    /// <param name="x">Its left.</param>
    /// <param name="y">Its top.</param>
    /// <param name="resource">Its text resource.</param>
    /// <param name="onEvent">What it does.</param>
    /// <returns>The link.</returns>
    public ComputerTextItem AddTab(int id, float x, float y, string resource, Action<ComputerItem, int> onEvent)
    {
        string text = Text(resource);
        IComputerFonts? fonts = World.Fonts;
        (float, float) size = fonts is null ? (text.Length * 8f, 16f) : (fonts.StringWidth(TabFont, text), fonts.LineHeight(TabFont));
        return Panel.Add(new ComputerTextItem(id, x, y, TabFont, text, size, 1, onEvent));
    }

    /// <summary>The font of the screens' links and headings.</summary>
    public const string TabFont = "font3.fnt";

    /// <summary>The font of the objectives' text and the labels on the map.</summary>
    public const string TextFont = "font4.fnt";

    /// <summary>The font of the log.</summary>
    public const string LogFont = "font1.fnt";

    /// <summary>A text resource, or the name itself when there is none.</summary>
    /// <param name="resource">Its name.</param>
    /// <returns>Its text.</returns>
    public string Text(string resource) => World.FindText(resource) ?? resource;

    /// <summary>
    /// Where the device is held and how it is turned (<c>0x46BD50</c>): ahead of the eye and a little below, its screen turned up
    /// to face it.
    /// </summary>
    /// <param name="eye">The eye, world units.</param>
    /// <param name="look">The eye's axes, by rows: its columns are across, ahead and up.</param>
    /// <returns>The device's position and axes.</returns>
    public static (Vector3d Position, Matrix3f Axes) DevicePose(Vector3d eye, Matrix3f look)
    {
        Matrix3f m = look;
        X87 dx = ((X87)m.M12 * DeviceForward) - ((X87)m.M13 * DeviceDown);
        X87 dy = ((X87)m.M22 * DeviceForward) - ((X87)m.M23 * DeviceDown);
        X87 dz = ((X87)m.M32 * DeviceForward) - ((X87)m.M33 * DeviceDown);
        Vector3d position = new(dx.Value + eye.X, dy.F + eye.Y, dz.F + eye.Z);
        float c = (float)Math.Cos(1.5707964f), s = (float)Math.Sin(1.5707964f);

        // The look times RotX(π/2), whose rows are (1, 0, 0), (0, c, -s), (0, s, c).
        Matrix3f axes = new(
            m.M11, (m.M12 * c) + (m.M13 * s), (m.M12 * -s) + (m.M13 * c),
            m.M21, (m.M22 * c) + (m.M23 * s), (m.M22 * -s) + (m.M23 * c),
            m.M31, (m.M32 * c) + (m.M33 * s), (m.M32 * -s) + (m.M33 * c));
        return (position, axes);
    }

    private void OpenScreen(ComputerScreenKind kind)
    {
        Panel = new ComputerPanel();
        Screen = kind switch
        {
            ComputerScreenKind.Map => new ComputerMap(this),
            ComputerScreenKind.Objectives => new ComputerObjectivesPage(this),
            ComputerScreenKind.Log => new ComputerLogPage(this),
            _ => null,
        };
    }

    // The x87's fistp, which the game's chopping control word makes a truncation.
    private static int RoundInt(float value) => (int)value;
}
