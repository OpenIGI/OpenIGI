using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Drives a <see cref="PlayerBody"/> from the keyboard and mouse, and gives the renderer its eye.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="FreeCamera"/>: where that flies, this one walks. It advances the body
/// on the <b>fixed thirty-hertz tick</b> rather than on real elapsed time, because the body's motion
/// is the game's own and has to stay deterministic — only the look is taken per frame, so the view
/// stays smooth at any frame rate.
/// </para>
/// <para>
/// <b>Movement is root motion.</b> There is no speed constant to set: the body moves by the animation's
/// own delta translation. Until the locomotion state machine is driving the animation choice, the walk
/// and run deltas here stand in for what the animation would carry — which is a divergence, and the one
/// place in the walking player where a number is chosen rather than read.
/// </para>
/// </remarks>
public sealed class PlayerCamera
{
    /// <summary>How far the mouse turns the view, in radians per unit of motion.</summary>
    public const float MouseSensitivity = 0.0035f;

    /// <summary>How fast the arrow keys turn, in radians a second.</summary>
    public const float KeyTurnRate = 1.8f;

    /// <summary>
    /// The delta translation a walk covers in one tick, in the body's frame.
    /// </summary>
    /// <remarks>
    /// <b>Divergence, temporary.</b> The animation carries this in retail. Chosen here so the two
    /// speeds are a plausible walk and run — about 2.2 and 5.5 metres a second once the
    /// <see cref="HumanMotion.DefaultDeltaTranslationScale"/> of 1.75 is applied.
    /// </remarks>
    public const float WalkDeltaPerTick = 170f;

    /// <summary>The delta translation a run covers in one tick.</summary>
    /// <remarks>See <see cref="WalkDeltaPerTick"/>; the same divergence.</remarks>
    public const float RunDeltaPerTick = 430f;

    /// <summary>World units in a metre.</summary>
    /// <remarks>
    /// The body works in the game's own integral world units, because that is what the collision
    /// octree indexes and what every constant read out of the executable is expressed in. The renderer
    /// works in metres. This is where the two meet, and keeping the conversion in one place is what
    /// stops a unit mistake reaching the physics.
    /// </remarks>
    public const double UnitsPerMetre = 4096.0;

    private readonly PlayerBody _body = new();
    private bool _wasCapturing;
    private bool _jumpHeld;

    /// <summary>Gets the body being driven.</summary>
    public PlayerBody Body => _body;

    /// <summary>Gets whether the cursor should be held while the view is being turned.</summary>
    public bool IsCapturingMouse { get; private set; }

    /// <summary>Gets the eye the scene is drawn from, in the renderer's metres.</summary>
    public Camera3D Camera => new(ToMetres(_body.Eye), Camera3D.LookRotation(_body.Yaw, _body.Pitch));

    /// <summary>Gets where the player is standing, in metres, for diagnostics.</summary>
    public Vector3d Position => ToMetres(_body.Position);

    /// <summary>Puts the player on the ground under a point.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="at">Where to try standing, in the renderer's metres.</param>
    /// <param name="yaw">Which way to face.</param>
    /// <returns><see langword="false"/> when there is no ground under the point.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    public bool Place(TerrainData terrain, Vector3d at, float yaw)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        _body.Position = new Vector3d(at.X * UnitsPerMetre, at.Y * UnitsPerMetre, at.Z * UnitsPerMetre);
        _body.Yaw = yaw;
        return _body.PlaceOnGround(terrain);
    }

    /// <summary>Puts the player at a spawn the level declared.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="spawn">The level's own <c>HumanPlayer</c> start.</param>
    /// <returns><see langword="false"/> when there is no ground under it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A spawn's position is already in the game's world units — unlike a scene placement, which the
    /// scene builder has divided into metres for the renderer — so it goes to the body untouched.
    /// </remarks>
    public bool PlaceAt(TerrainData terrain, PlayerSpawnPoint spawn)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        _body.Position = spawn.Position;
        _body.Yaw = spawn.Gamma;
        _body.Pitch = 0f;
        return _body.PlaceOnGround(terrain);
    }

    private static Vector3d ToMetres(Vector3d units) =>
        new(units.X / UnitsPerMetre, units.Y / UnitsPerMetre, units.Z / UnitsPerMetre);

    /// <summary>Takes the look from this frame's mouse motion.</summary>
    /// <param name="input">The window's input.</param>
    /// <param name="seconds">Real seconds since the last call, for the key turn.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Looking is per frame and movement is per tick, deliberately: the view should be as smooth as the
    /// display allows, while the body must step identically however fast the frames come.
    /// </remarks>
    public void Look(InputState input, float seconds)
    {
        ArgumentNullException.ThrowIfNull(input);

        MouseState mouse = input.Mouse;
        bool looking = mouse.IsDown(MouseButton.Right);

        // The frame a button goes down is skipped: capturing the cursor warps it, and that warp arrives
        // as a movement the player never made. FreeCamera does the same for the same reason.
        bool settling = looking && !_wasCapturing;
        _wasCapturing = looking;
        IsCapturingMouse = looking;

        if (looking && !settling)
        {
            _body.Yaw -= mouse.DeltaX * MouseSensitivity;
            _body.Pitch = Math.Clamp(
                _body.Pitch - (mouse.DeltaY * MouseSensitivity),
                -PlayerBody.PitchLimit,
                PlayerBody.PitchLimit);
        }

        KeyboardState keys = input.Keyboard;
        _body.Yaw -= Axis(keys, KeyCode.Right, KeyCode.Left) * seconds * KeyTurnRate;
    }

    /// <summary>Advances the body by one simulation tick.</summary>
    /// <param name="input">The window's input.</param>
    /// <param name="terrain">The level's terrain.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Tick(InputState input, TerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(terrain);

        KeyboardState keys = input.Keyboard;

        float forward = Axis(keys, KeyCode.W, KeyCode.S);
        float strafe = Axis(keys, KeyCode.D, KeyCode.A);
        bool run = keys.IsDown(KeyCode.LeftShift) || keys.IsDown(KeyCode.RightShift);
        bool crouch = keys.IsDown(KeyCode.LeftControl) || keys.IsDown(KeyCode.RightControl);

        // Jump is the rising edge, not the level, so holding it does not bounce.
        bool jumpDown = keys.IsDown(KeyCode.Space);
        bool jump = jumpDown && !_jumpHeld;
        _jumpHeld = jumpDown;

        PlayerCommand command = new(forward, strafe, 0f, 0f, run, crouch, jump);

        float speed = run ? RunDeltaPerTick : WalkDeltaPerTick;
        if (crouch)
        {
            speed *= 0.5f;
        }

        // Diagonal movement is normalised, or walking into a corner would be faster than walking at a
        // wall - a mistake the original does not make because its animations carry the translation.
        Vector3f delta = new(forward * speed, strafe * speed, 0f);
        float lengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
        if (lengthSquared > speed * speed)
        {
            float scale = speed / MathF.Sqrt(lengthSquared);
            delta = new Vector3f(delta.X * scale, delta.Y * scale, 0f);
        }

        _body.Tick(command, terrain, delta);
    }

    private static float Axis(KeyboardState keys, KeyCode positive, KeyCode negative)
    {
        float value = 0f;
        if (keys.IsDown(positive))
        {
            value += 1f;
        }

        if (keys.IsDown(negative))
        {
            value -= 1f;
        }

        return value;
    }
}
