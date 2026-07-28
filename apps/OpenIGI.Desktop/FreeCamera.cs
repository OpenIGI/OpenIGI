using OpenIGI.Core.Numerics;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>A fly-through camera with a scene editor's controls.</summary>
/// <remarks>
/// <para>
/// A development tool, not a game camera: the original's is a task with a state machine for
/// first-person, third-person, binocular and map views (<c>0x482700</c>, M8). This exists to look at a
/// level that has no player in it yet.
/// </para>
/// <para>
/// The mapping is the one a Unity scene view uses, and the point of it is that <b>nothing moves the
/// camera unless a mouse button is held</b> — the keyboard alone never does, so a stray key press while
/// looking at something cannot drift the view:
/// </para>
/// <list type="table">
/// <item><term>Hold right mouse</term><description>look around, and <b>W A S D</b> fly while it is held; <b>Q E</b> drop and rise.</description></item>
/// <item><term>Hold middle mouse</term><description>pan — the world follows the pointer.</description></item>
/// <item><term>Wheel</term><description>dolly along the view; while right mouse is held it trims the fly speed instead, and the trim sticks.</description></item>
/// <item><term>Shift</term><description>flat move: fly without changing height.</description></item>
/// <item><term>Ctrl</term><description>slower, for placing the view precisely.</description></item>
/// <item><term>Arrow keys</term><description>turn without the mouse, so a keyboard-only session still works.</description></item>
/// </list>
/// <para>
/// Two of the gist's features are not here because they need something the engine does not have yet:
/// double-click to focus wants a collision raycast to pick an object with (M9), and its undo restores
/// only the position that focus moved from.
/// </para>
/// </remarks>
public sealed class FreeCamera
{
    /// <summary>Metres per second at the default speed.</summary>
    public const float DefaultSpeed = 12f;

    /// <summary>The slowest the wheel can set the fly speed to, in metres per second.</summary>
    public const float MinimumSpeed = 0.5f;

    /// <summary>The fastest the wheel can set the fly speed to, in metres per second.</summary>
    public const float MaximumSpeed = 400f;

    /// <summary>The multiplier held control applies.</summary>
    public const float CreepMultiplier = 0.2f;

    /// <summary>What one notch of the wheel multiplies the fly speed by, while looking.</summary>
    public const float SpeedStep = 1.15f;

    /// <summary>How far one notch of the wheel dollies, as a fraction of a second's travel.</summary>
    public const float ZoomStep = 0.2f;

    /// <summary>How far one pixel of a pan drags, as a fraction of a second's travel.</summary>
    public const float PanStep = 0.01f;

    /// <summary>Radians of yaw per pixel of mouse motion.</summary>
    public const float MouseSensitivity = 0.0035f;

    /// <summary>Radians per second the arrow keys turn at.</summary>
    public const float KeyTurnRate = 1.5f;

    /// <summary>How far the camera may look up or down, in radians.</summary>
    /// <remarks>Just short of straight up, so the view never flips through the pole.</remarks>
    public const float PitchLimit = 1.55f;

    /// <summary>The largest mouse movement, in pixels along one axis, that is treated as real.</summary>
    /// <remarks>
    /// Capturing the cursor warps it, and the backend reports that warp as an ordinary move — so the
    /// first report after the capture engages is a jump of arbitrary size that has nothing to do with
    /// the user's hand. A frame's worth of real movement is tens of pixels; anything past this is that
    /// warp, or a backend switching between window and virtual coordinates, and folding it into the
    /// angle throws the view somewhere absurd.
    /// </remarks>
    public const float MaximumFrameMotion = 400f;

    private float _yaw;
    private float _pitch;
    private bool _wasCapturing;

    /// <summary>Initializes a new instance of the <see cref="FreeCamera"/> class.</summary>
    /// <param name="position">The starting position, in metres.</param>
    /// <param name="yaw">The starting yaw in radians; zero looks along +Y.</param>
    /// <param name="pitch">The starting pitch in radians.</param>
    public FreeCamera(Vector3d position, float yaw = 0f, float pitch = 0f)
    {
        Position = position;
        _yaw = Wrap(yaw);
        _pitch = Math.Clamp(pitch, -PitchLimit, PitchLimit);
    }

    /// <summary>Gets the camera's world position, in metres.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets or sets the fly speed in metres per second, before any modifier.</summary>
    public float Speed { get; set; } = DefaultSpeed;

    /// <summary>Gets a value indicating whether a mouse button is held and the pointer should be captured.</summary>
    /// <remarks>The caller hides and locks the cursor while this is set; see the desktop shell's frame loop.</remarks>
    public bool IsCapturingMouse { get; private set; }

    /// <summary>Gets the camera as the renderer wants it.</summary>
    public Camera3D Camera => new(Position, Camera3D.LookRotation(_yaw, _pitch));

    /// <summary>Advances the camera by one frame of input.</summary>
    /// <param name="input">The input state.</param>
    /// <param name="seconds">How long the frame lasted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public void Update(InputState input, float seconds)
    {
        ArgumentNullException.ThrowIfNull(input);

        MouseState mouse = input.Mouse;
        KeyboardState keys = input.Keyboard;

        bool looking = mouse.IsDown(MouseButton.Right);
        bool panning = mouse.IsDown(MouseButton.Middle);
        bool capturing = looking || panning;

        // The frame a button goes down is skipped: capturing the cursor warps it, and that warp arrives
        // as a movement the user never made.
        bool settling = capturing && !_wasCapturing;
        _wasCapturing = capturing;
        IsCapturingMouse = capturing;

        float deltaX = settling ? 0f : Motion(mouse.DeltaX);
        float deltaY = settling ? 0f : Motion(mouse.DeltaY);
        float wheel = settling ? 0f : Motion(mouse.WheelDelta, 10f);

        if (looking)
        {
            _yaw += deltaX * MouseSensitivity;
            _pitch = Math.Clamp(_pitch - (deltaY * MouseSensitivity), -PitchLimit, PitchLimit);
        }

        // Arrow keys turn whether or not the mouse is held, so the camera is usable without it.
        _yaw += Axis(keys, KeyCode.Right, KeyCode.Left) * seconds * KeyTurnRate;
        _pitch = Math.Clamp(
            _pitch + (Axis(keys, KeyCode.Up, KeyCode.Down) * seconds * KeyTurnRate),
            -PitchLimit,
            PitchLimit);

        // Yaw is the one angle with no natural stop, so it is wrapped rather than left to accumulate.
        // A turn is periodic and an angle that grows without bound eventually loses every bit of
        // precision it has, which turns a smooth pan into a stutter and then into nonsense.
        _yaw = Wrap(_yaw);

        // The wheel trims the fly speed while looking, as a scene view's does, and the trim sticks.
        if (looking && wheel != 0f)
        {
            Speed = Math.Clamp(Speed * MathF.Pow(SpeedStep, wheel), MinimumSpeed, MaximumSpeed);
            wheel = 0f;
        }

        // The rows of the view rotation are the view axes in world space, so they are the directions to
        // travel along.
        Matrix3f rotation = Camera3D.LookRotation(_yaw, _pitch);
        Vector3f right = rotation.Row1;
        Vector3f up = rotation.Row2;
        Vector3f ahead = rotation.Row3;

        bool flat = keys.IsDown(KeyCode.LeftShift) || keys.IsDown(KeyCode.RightShift);
        float speed = Speed * seconds;
        if (keys.IsDown(KeyCode.LeftControl) || keys.IsDown(KeyCode.RightControl))
        {
            speed *= CreepMultiplier;
        }

        double moveX = 0.0;
        double moveY = 0.0;
        double moveZ = 0.0;

        // Flying is gated on the right button, the way a scene view gates it: the keyboard on its own
        // never moves the camera.
        if (looking)
        {
            float forward = Axis(keys, KeyCode.W, KeyCode.S);
            float strafe = Axis(keys, KeyCode.D, KeyCode.A);
            float lift = Axis(keys, KeyCode.E, KeyCode.Q);

            moveX = ((ahead.X * forward) + (right.X * strafe)) * speed;
            moveY = ((ahead.Y * forward) + (right.Y * strafe)) * speed;

            // Lift is along the world's vertical rather than the camera's, so flying while looking down
            // does not corkscrew.
            moveZ = (((ahead.Z * forward) + (right.Z * strafe)) + lift) * speed;
        }

        // Panning drags the world with the pointer, so the camera goes the other way.
        if (panning)
        {
            float panScale = Speed * PanStep;
            moveX += ((-right.X * deltaX) + (up.X * deltaY)) * panScale;
            moveY += ((-right.Y * deltaX) + (up.Y * deltaY)) * panScale;
            moveZ += ((-right.Z * deltaX) + (up.Z * deltaY)) * panScale;
        }

        // The wheel dollies along the view when it is not being used to trim the speed.
        if (wheel != 0f)
        {
            float zoom = Speed * ZoomStep * wheel;
            moveX += ahead.X * zoom;
            moveY += ahead.Y * zoom;
            moveZ += ahead.Z * zoom;
        }

        if (flat)
        {
            // Flat move keeps the height it started at, which is what makes it useful for crossing a
            // level without drifting into the ground or the sky.
            moveZ = 0.0;
        }

        if (moveX == 0.0 && moveY == 0.0 && moveZ == 0.0)
        {
            return;
        }

        Position = new Vector3d(Position.X + moveX, Position.Y + moveY, Position.Z + moveZ);
    }

    private static float Axis(KeyboardState keys, KeyCode positive, KeyCode negative) =>
        (keys.IsDown(positive) ? 1f : 0f) - (keys.IsDown(negative) ? 1f : 0f);

    /// <summary>Takes a frame's mouse movement, discarding anything that cannot be a hand.</summary>
    private static float Motion(float delta, float limit = MaximumFrameMotion) =>
        float.IsFinite(delta) && Math.Abs(delta) <= limit ? delta : 0f;

    /// <summary>Folds an angle back into <c>[-π, π]</c>.</summary>
    private static float Wrap(float radians)
    {
        if (!float.IsFinite(radians))
        {
            return 0f;
        }

        float wrapped = MathF.IEEERemainder(radians, MathF.Tau);
        return float.IsFinite(wrapped) ? wrapped : 0f;
    }
}
