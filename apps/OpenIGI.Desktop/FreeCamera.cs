using OpenIGI.Core.Numerics;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>A fly-through camera driven by the keyboard and mouse.</summary>
/// <remarks>
/// A development tool, not a game camera: the original's is a task with a state machine for
/// first-person, third-person, binocular and map views (<c>0x482700</c>, M8). This exists to look at a
/// level that has no player in it yet.
/// </remarks>
public sealed class FreeCamera
{
    /// <summary>Metres per second at a walk.</summary>
    public const float WalkSpeed = 12f;

    /// <summary>The multiplier held shift applies.</summary>
    public const float SprintMultiplier = 6f;

    /// <summary>Radians of yaw per pixel of mouse motion.</summary>
    public const float MouseSensitivity = 0.0035f;

    private float _yaw;
    private float _pitch;

    /// <summary>Initializes a new instance of the <see cref="FreeCamera"/> class.</summary>
    /// <param name="position">The starting position, in metres.</param>
    /// <param name="yaw">The starting yaw in radians; zero looks along +Y.</param>
    /// <param name="pitch">The starting pitch in radians.</param>
    public FreeCamera(Vector3d position, float yaw = 0f, float pitch = 0f)
    {
        Position = position;
        _yaw = yaw;
        _pitch = pitch;
    }

    /// <summary>Gets the camera's world position, in metres.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets the camera as the renderer wants it.</summary>
    public Camera3D Camera => new(Position, Camera3D.LookRotation(_yaw, _pitch));

    /// <summary>Advances the camera by one frame of input.</summary>
    /// <param name="input">The input state.</param>
    /// <param name="seconds">How long the frame lasted.</param>
    /// <param name="mouseLook">Whether mouse motion turns the camera.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public void Update(InputState input, float seconds, bool mouseLook)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (mouseLook)
        {
            _yaw += input.Mouse.DeltaX * MouseSensitivity;
            _pitch = Math.Clamp(_pitch - (input.Mouse.DeltaY * MouseSensitivity), -1.5f, 1.5f);
        }

        KeyboardState keys = input.Keyboard;
        float forward = Axis(keys, KeyCode.W, KeyCode.S);
        float strafe = Axis(keys, KeyCode.D, KeyCode.A);
        float rise = Axis(keys, KeyCode.E, KeyCode.Q);

        // Arrow keys turn, so the camera is usable without capturing the mouse.
        _yaw += Axis(keys, KeyCode.Right, KeyCode.Left) * seconds * 1.5f;
        _pitch = Math.Clamp(_pitch + (Axis(keys, KeyCode.Up, KeyCode.Down) * seconds * 1.5f), -1.5f, 1.5f);

        if (forward == 0f && strafe == 0f && rise == 0f)
        {
            return;
        }

        float speed = WalkSpeed * seconds * (keys.IsDown(KeyCode.LeftShift) ? SprintMultiplier : 1f);

        // The rows of the view rotation are the view axes in world space, so they are exactly the
        // directions to move along.
        Matrix3f rotation = Camera3D.LookRotation(_yaw, _pitch);
        Vector3f right = rotation.Row1;
        Vector3f up = rotation.Row2;
        Vector3f ahead = rotation.Row3;

        Position = new Vector3d(
            Position.X + (((ahead.X * forward) + (right.X * strafe) + (up.X * rise)) * speed),
            Position.Y + (((ahead.Y * forward) + (right.Y * strafe) + (up.Y * rise)) * speed),
            Position.Z + (((ahead.Z * forward) + (right.Z * strafe) + (up.Z * rise)) * speed));
    }

    private static float Axis(KeyboardState keys, KeyCode positive, KeyCode negative) =>
        (keys.IsDown(positive) ? 1f : 0f) - (keys.IsDown(negative) ? 1f : 0f);
}
