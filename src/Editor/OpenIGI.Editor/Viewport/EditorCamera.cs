using OpenIGI.Core.Numerics;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Editor.Viewport;

/// <summary>What a held mouse button is doing to the editor camera.</summary>
internal enum CameraDrag
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Right button: looking around, and flying with W A S D, Q and E.</summary>
    Look,

    /// <summary>Middle button: sliding the view sideways and up and down.</summary>
    Pan,

    /// <summary>Alt and the left button: turning around a point in front of the camera.</summary>
    Orbit,
}

/// <summary>
/// The editor's camera controls, after the ones scene editors share (s&amp;box's among them): look and fly on the right
/// button, pan on the middle one, orbit on Alt and the left one, the wheel to dolly or, while flying, to set the speed, and a
/// glide to whatever is framed. Movement eases toward where it is going rather than jumping there.
/// </summary>
/// <remarks>
/// It drives the game's free camera only through <see cref="FreeCamera.MoveTo"/>, so the camera the renderer draws from is
/// the one it always was; only what moves it in the editor is new.
/// </remarks>
internal sealed class EditorCamera
{
    /// <summary>Radians turned per pixel the mouse moves.</summary>
    private const float Sensitivity = 0.0035f;

    /// <summary>How far the camera may look up or down, short of straight, in radians.</summary>
    private const float PitchLimit = 1.55f;

    /// <summary>How long the camera takes to catch up with where flying puts it, in seconds.</summary>
    private const double FlySmoothing = 0.06;

    /// <summary>How long a glide to a framed object takes to settle, in seconds.</summary>
    private const double GlideSmoothing = 0.18;

    private Vector3d? _target;
    private Vector3d _velocity;
    private double _smoothing = FlySmoothing;
    private bool _settling;

    /// <summary>Gets what the held button is doing.</summary>
    public CameraDrag Drag { get; private set; }

    /// <summary>Gets or sets how far in front of the camera orbiting turns around and dollying heads for, in metres.</summary>
    public double PivotDistance { get; set; } = 20.0;

    /// <summary>Gets how long ago the flying speed was last changed, so a readout can show it for a moment.</summary>
    public double SecondsSinceSpeedChange { get; private set; } = double.MaxValue;

    /// <summary>Starts a drag.</summary>
    /// <param name="drag">What the button does.</param>
    public void Begin(CameraDrag drag)
    {
        Drag = drag;

        // The first frame's motion is the jump the cursor makes as it is locked, not a movement anyone made.
        _settling = true;
    }

    /// <summary>Ends the drag.</summary>
    public void End() => Drag = CameraDrag.None;

    /// <summary>Moves the camera for a frame: the drag, the keys while flying, and any glide still under way.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="input">The frame's input.</param>
    /// <param name="seconds">The frame time.</param>
    public void Update(FreeCamera camera, InputState input, float seconds)
    {
        SecondsSinceSpeedChange += seconds;
        float dx = _settling ? 0f : input.Mouse.DeltaX;
        float dy = _settling ? 0f : input.Mouse.DeltaY;
        _settling = false;

        float yaw = camera.Yaw;
        float pitch = camera.Pitch;
        Vector3d position = camera.Position;
        (Vector3d forward, Vector3d right, Vector3d up) = Axes(yaw, pitch);

        switch (Drag)
        {
            case CameraDrag.Look:
            {
                yaw += dx * Sensitivity;
                pitch = Math.Clamp(pitch - (dy * Sensitivity), -PitchLimit, PitchLimit);
                (forward, right, _) = Axes(yaw, pitch);
                Vector3d move = (forward * Axis(input, KeyCode.W, KeyCode.S))
                    + (right * Axis(input, KeyCode.D, KeyCode.A))
                    + (new Vector3d(0, 0, 1) * Axis(input, KeyCode.E, KeyCode.Q));
                if (move.Length > 0.0)
                {
                    double speed = camera.Speed
                        * (input.Keyboard.IsDown(KeyCode.LeftShift) ? 4.0 : 1.0)
                        * (input.Keyboard.IsDown(KeyCode.LeftControl) ? 0.25 : 1.0);
                    _smoothing = FlySmoothing;
                    _target = (_target ?? position) + (move * (1.0 / move.Length) * speed * seconds);
                }

                break;
            }

            case CameraDrag.Pan:
            {
                // A pixel moves the view by about what a pixel covers at the pivot, so what is under the cursor follows it.
                double perPixel = Math.Max(0.002, PivotDistance * 0.0012);
                position += (right * (-dx * perPixel)) + (up * (dy * perPixel));
                _target = null;
                break;
            }

            case CameraDrag.Orbit:
            {
                Vector3d pivot = position + (forward * PivotDistance);
                yaw += dx * Sensitivity;
                pitch = Math.Clamp(pitch - (dy * Sensitivity), -PitchLimit, PitchLimit);
                (forward, _, _) = Axes(yaw, pitch);
                position = pivot - (forward * PivotDistance);
                _target = null;
                break;
            }
        }

        if (_target is { } target)
        {
            position = SmoothDamp(position, target, ref _velocity, _smoothing, seconds);
            if ((position - target).Length < 0.001)
            {
                position = target;
                _target = null;
                _velocity = Vector3d.Zero;
            }
        }

        camera.MoveTo(position, yaw, pitch);
    }

    /// <summary>Steps the flying speed, finer at low speeds and coarser at high ones.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="notches">The wheel's movement.</param>
    public void ChangeSpeed(FreeCamera camera, float notches)
    {
        float speed = camera.Speed;
        float step = speed < 5f ? 0.5f : speed < 20f ? 1f : MathF.Max(2.5f, MathF.Round(speed * 0.1f / 2.5f) * 2.5f);
        camera.Speed = Math.Clamp(speed + (step * MathF.Sign(notches)), FreeCamera.MinimumSpeed, FreeCamera.MaximumSpeed);
        SecondsSinceSpeedChange = 0.0;
    }

    /// <summary>Moves the camera toward the pivot or away from it, by a share of the distance so it slows as it nears.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="notches">The wheel's movement; positive moves in.</param>
    public void Dolly(FreeCamera camera, float notches)
    {
        double step = Math.Max(0.25, PivotDistance * 0.15) * notches;
        (Vector3d forward, _, _) = Axes(camera.Yaw, camera.Pitch);
        Vector3d from = _target ?? camera.Position;
        PivotDistance = Math.Max(0.5, PivotDistance - step);
        _smoothing = FlySmoothing;
        _target = from + (forward * step);
    }

    /// <summary>Glides the camera to look at a point from far enough away to see something of a size.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="point">The point, in world metres.</param>
    /// <param name="radiusMetres">How big what is there is.</param>
    public void Focus(FreeCamera camera, Vector3d point, double radiusMetres)
    {
        double distance = Math.Clamp(radiusMetres * 2.4, 3.0, 400.0);
        (Vector3d forward, _, _) = Axes(camera.Yaw, camera.Pitch);
        PivotDistance = distance;
        _smoothing = GlideSmoothing;
        _target = point - (forward * distance);
    }

    /// <summary>Gets the camera's forward, right and up axes for a yaw and a pitch, as the renderer builds them.</summary>
    /// <param name="yaw">The yaw.</param>
    /// <param name="pitch">The pitch.</param>
    /// <returns>The three axes.</returns>
    public static (Vector3d Forward, Vector3d Right, Vector3d Up) Axes(float yaw, float pitch)
    {
        (double sy, double cy) = Math.SinCos(yaw);
        (double sp, double cp) = Math.SinCos(pitch);
        return (new Vector3d(sy * cp, cy * cp, sp), new Vector3d(cy, -sy, 0), new Vector3d(-sy * sp, -cy * sp, cp));
    }

    private static double Axis(InputState input, KeyCode positive, KeyCode negative) =>
        (input.Keyboard.IsDown(positive) ? 1.0 : 0.0) - (input.Keyboard.IsDown(negative) ? 1.0 : 0.0);

    // A critically damped spring toward the target: the easing that keeps up with a moving target without overshooting it.
    private static Vector3d SmoothDamp(Vector3d current, Vector3d target, ref Vector3d velocity, double smoothTime, double seconds)
    {
        double omega = 2.0 / Math.Max(1e-4, smoothTime);
        double x = omega * seconds;
        double decay = 1.0 / (1.0 + x + (0.48 * x * x) + (0.235 * x * x * x));
        Vector3d change = current - target;
        Vector3d temp = (velocity + (change * omega)) * seconds;
        velocity = (velocity - (temp * omega)) * decay;
        return target + ((change + temp) * decay);
    }
}
