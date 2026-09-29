using OpenIGI.Core.Configuration;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The player's flashlight: the switch, where the torch hangs and points, and handing it to the renderer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not retail.</b> The original has no flashlight. It is the remaster's, on the Flashlight action (F by
/// default), and it exists only where the renderer can light with it: <c>r_flashlight</c>, which vanilla
/// pins off, and the deferred path, which vanilla does not draw. Where it cannot exist the key does nothing
/// at all -- no light and no click -- so a vanilla run is the original and nothing else.
/// </para>
/// <para>
/// The torch hangs off the body rather than the camera: a hand's width to the right of the eye, a little
/// below it and a little ahead, as a light clipped to a chest rig is. That offset is what makes its shadows
/// visible at all -- a light at the eye casts every shadow straight behind what casts it -- and it stays with
/// the body in third person, where the camera is somewhere else entirely. It aims where the crosshair lands,
/// trails the view by a few hundredths of a second and sways with the gait (<see cref="FlashlightRig"/>).
/// </para>
/// </remarks>
internal sealed partial class GameHost
{
    /// <summary>Where the torch hangs from the eye, in metres along the view's right, up and forward.</summary>
    private static readonly Vector3f FlashlightMount = new(0.16f, -0.14f, 0.10f);

    /// <summary>How far down the view the beam looks for what it is pointed at, in metres.</summary>
    private const double FlashlightProbeMetres = 20.0;

    /// <summary>The farthest the beam converges on the crosshair, in metres; past it the beam runs parallel.</summary>
    private const float FlashlightConvergeMetres = 12f;

    /// <summary>What a surface the beam lands on sends back, as a fraction: a painted wall, roughly.</summary>
    private const float FlashlightBounceAlbedo = 0.35f;

    /// <summary>The share of the beam the reflector leaks straight onto the hand holding it.</summary>
    private const float FlashlightLeak = 0.004f;

    private readonly FlashlightRig _flashlightRig = new();
    private bool _flashlightShown;
    private int _flashlightMilliseconds;
    private Vector3d? _flashlightLastBody;
    private float _flashlightConverge = FlashlightConvergeMetres;
    private float _flashlightWallMetres = float.PositiveInfinity;

    /// <summary>Gets whether the renderer can light with a flashlight at all this frame.</summary>
    private static bool FlashlightAvailable =>
        RenderCVars.Flashlight.Value && RenderCVars.OffscreenScene.Value && RenderCVars.Deferred.Value;

    /// <summary>Registers the console's handle on the flashlight.</summary>
    /// <param name="console">The console to register with.</param>
    /// <remarks>
    /// For a headless capture and a script as much as for a player: <c>+flashlight 1</c> on the command line
    /// switches it on without a key. The key itself is the Flashlight action in the Controls page, not a
    /// console bind, so it can be rebound with the rest of the controls.
    /// </remarks>
    private void RegisterFlashlightCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "flashlight",
            "Switches the player's flashlight: 1 on, 0 off, nothing to toggle. Not in the original.",
            context =>
            {
                if (!FlashlightAvailable)
                {
                    context.Warn("The flashlight needs a graphics preset above vanilla (r_flashlight and the deferred path).");
                    return;
                }

                bool on = context.Count > 0
                    ? context[0] is "1" or "on" or "true"
                    : !_flashlightRig.IsOn;

                if (on != _flashlightRig.IsOn)
                {
                    _flashlightRig.Toggle();
                }

                context.Print(on ? "Flashlight on." : "Flashlight off.");
            });
    }

    /// <summary>Reads the Flashlight action and flips the switch, with its click.</summary>
    /// <param name="menuMode">Whether a menu holds the input.</param>
    /// <param name="consoleOpen">Whether the console does.</param>
    /// <remarks>
    /// Read per frame beside the map computer's key rather than on the thirty-hertz tick, so a quick tap is
    /// never missed. Only while the light could be seen -- in the player's own view, alive, with nothing else
    /// holding the picture -- so the key cannot switch it on unseen behind a cutscene.
    /// </remarks>
    private void UpdateFlashlightSwitch(bool menuMode, bool consoleOpen)
    {
        if (!FlashlightAvailable)
        {
            // A preset without it put the light out; it does not come back on by itself when the preset does.
            _flashlightRig.ForceOff();
            return;
        }

        if (_playerDead)
        {
            _flashlightRig.ForceOff();
            return;
        }

        if (menuMode || consoleOpen || !_flashlightShown || _player is not { } player)
        {
            return;
        }

        player.InputProfile = _configuration.Active;
        if (!player.WasActionPressed(_window.Input, PlayerAction.Flashlight))
        {
            return;
        }

        bool on = _flashlightRig.Toggle();
        _sounds?.PlayExternal(FlashlightClick.SoundName, gain: 0.8f, priority: 180, pitch: on ? 1f : 0.92f);
    }

    /// <summary>Puts the flashlight out and forgets its aim: a mission starting, restarting or ending.</summary>
    private void ResetFlashlight()
    {
        _flashlightRig.ForceOff();
        _flashlightRig.ResetAim();
        _flashlightLastBody = null;
        _flashlightConverge = FlashlightConvergeMetres;
        _flashlightWallMetres = float.PositiveInfinity;
    }

    /// <summary>Places, aims and hands this frame's flashlight to the renderer, or tells it there is none.</summary>
    /// <param name="shown">Whether the player's own view is on screen, where the light can be.</param>
    /// <param name="paused">Whether the world is paused behind a menu, which freezes the torch where it is.</param>
    /// <param name="body">The body's own eye, before any third-person camera moved away from it.</param>
    /// <param name="eye">The eye the frame is drawn from, whose view space the renderer works in.</param>
    /// <param name="thirdPerson">Whether the camera is away from the body.</param>
    private void SubmitFlashlight(bool shown, bool paused, in Camera3D body, in Camera3D eye, bool thirdPerson)
    {
        _flashlightShown = shown && _player is not null && !_playerDead;

        int now = _loop.TimeSource.Milliseconds;
        float seconds = paused ? 0f : Math.Clamp((now - _flashlightMilliseconds) / 1000f, 0f, 0.1f);
        _flashlightMilliseconds = now;

        if (!_flashlightShown || !FlashlightAvailable)
        {
            _flashlightRig.ResetAim();
            _flashlightLastBody = null;
            return;
        }

        Vector3f right = body.Orientation.Row1;
        Vector3f up = body.Orientation.Row2;
        Vector3f forward = body.Orientation.Row3;

        // How far the body moved this frame, along the ground, which is what the sway rides.
        float moved = 0f;
        if (_flashlightLastBody is { } last)
        {
            double dx = body.Position.X - last.X;
            double dy = body.Position.Y - last.Y;
            moved = (float)Math.Min(Math.Sqrt((dx * dx) + (dy * dy)), 2.0);
        }

        _flashlightLastBody = body.Position;
        float pace = seconds > 0f ? Math.Clamp(moved / seconds / 5f, 0f, 1f) : 0f;

        Vector3d lens = body.Position
            + (ToDouble(right) * FlashlightMount.X)
            + (ToDouble(up) * FlashlightMount.Y)
            + (ToDouble(forward) * FlashlightMount.Z);

        // What the crosshair is on, and how far: the beam converges there, and a near wall throws the beam
        // back onto the hands. The lens is also kept out of any wall the body is pressed against.
        DeathCameraVantage.Blocked? walls = ThirdPersonWalls();
        float hit = (float)FlashlightProbeMetres;
        if (walls is not null)
        {
            Vector3d from = body.Position * WorldScene.UnitsPerMetre;
            if (walls(from, lens * WorldScene.UnitsPerMetre, out Vector3d blockedAt))
            {
                Vector3d inward = (blockedAt / WorldScene.UnitsPerMetre) - body.Position;
                double length = inward.Length;
                lens = length > 0.06 ? body.Position + (inward * ((length - 0.05) / length)) : body.Position;
            }

            Vector3d ahead = body.Position + (ToDouble(forward) * FlashlightProbeMetres);
            if (walls(from, ahead * WorldScene.UnitsPerMetre, out Vector3d landed))
            {
                hit = (float)((landed / WorldScene.UnitsPerMetre) - body.Position).Length;
            }
        }

        // Smoothed, so the beam does not jump its aim as the crosshair crosses a pole.
        float convergeKeep = seconds > 0f ? MathF.Pow(0.5f, seconds / 0.08f) : 1f;
        _flashlightConverge = (_flashlightConverge * convergeKeep)
            + (MathF.Min(MathF.Max(hit, 1f), FlashlightConvergeMetres) * (1f - convergeKeep));
        _flashlightWallMetres = hit;

        Vector3d focus = body.Position + (ToDouble(forward) * _flashlightConverge);
        Vector3f target = (focus - lens).ToVector3f();

        _flashlightRig.Update(
            seconds,
            target,
            right,
            up,
            moved,
            pace,
            RenderCVars.FlashlightLag.Value,
            RenderCVars.FlashlightSway.Value);

        if (_flashlightRig.Level <= 0f)
        {
            return;
        }

        // Into the drawn eye's view space, the camera-relative subtract done in double precision as the
        // lamps' is: the level sits far from the world origin.
        Matrix3f worldToView = eye.Orientation.Transposed();
        Vector3f lensView = worldToView.Transform((lens - eye.Position).ToVector3f());
        Vector3f axisView = worldToView.Transform(_flashlightRig.Aim);
        Vector3f upView = worldToView.Transform(up);

        Vector3f colour = ColourTemperature.ToLinearRgb(RenderCVars.FlashlightKelvin.Value);
        float intensity = RenderCVars.FlashlightIntensity.Value * _flashlightRig.Level;
        float outer = RenderCVars.FlashlightOuter.Value;

        // What reaches the hands and the gun: a sliver the reflector leaks onto the hand holding it, and the
        // spot the beam lights on whatever it is pointed at, sent back. The spot's area grows as the square
        // of its distance and its brightness falls by the same, so what it returns falls as one over the
        // distance squared -- nothing from a wall across a courtyard, a great deal from a door at arm's length.
        float wall = MathF.Max(_flashlightWallMetres, 0.35f);
        float spread = MathF.Tan(outer * (MathF.PI / 180f));
        float bounce = _flashlightWallMetres < (float)FlashlightProbeMetres
            ? FlashlightBounceAlbedo * spread * spread / (wall * wall)
            : 0f;
        Vector3f rigFill = colour * (intensity * (FlashlightLeak + bounce) * RenderCVars.FlashlightRig.Value);

        _device.SetFlashlight(new FlashlightLight(
            lensView,
            axisView,
            upView,
            colour,
            intensity,
            RenderCVars.FlashlightRange.Value,
            RenderCVars.FlashlightInner.Value,
            outer,
            RenderCVars.FlashlightSpill.Value,
            RenderCVars.FlashlightSpillLevel.Value,
            RenderCVars.FlashlightLens.Value,
            NearCore: 0.3f,

            // Past the lens is the hand, the gun and -- in third person, where they are drawn -- the body.
            Clearance: thirdPerson ? 0.35f : 0.12f,
            Shadows: RenderCVars.FlashlightShadows.Value != 0,
            Volumetric: RenderCVars.FlashlightFog.Value,
            Dust: RenderCVars.FlashlightDust.Value,
            RigFill: thirdPerson ? Vector3f.Zero : rigFill));
    }

    private static Vector3d ToDouble(Vector3f v) => new(v.X, v.Y, v.Z);
}
