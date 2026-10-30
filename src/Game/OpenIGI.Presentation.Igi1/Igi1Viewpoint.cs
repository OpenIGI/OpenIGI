using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Igi1.Cameras;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Project I.G.I.'s player's viewpoint: the walking eye, the death camera over the body (HumanCamera death mode), the mode-3
/// orbit around a climbing or interacting body, and the remaster's move over the shoulder.
/// </summary>
/// <param name="gameplay">The first game's mission, whose player it looks through.</param>
internal sealed class Igi1Viewpoint(Igi1Gameplay gameplay) : IMissionViewpoint
{
    private readonly ThirdPersonCamera _thirdPerson = new();
    private Igi1HumanCamera _death = new();
    private Igi1HumanCamera _interaction = new();
    private Vector3d _interactionFeet;

    // Where the orbit looks: the body's eye (HumanCamera's info.pos, HumanView_GetEye), standing over its feet.
    private static Vector3d EyeOver(Vector3d feet) => new(feet.X, feet.Y, feet.Z + PlayerBody.StandingEyeHeight);

    // A corpse's feet under the centre the death camera follows.
    private static Vector3d FeetUnder(Vector3d centre) => new(centre.X, centre.Y, centre.Z - (PlayerBody.StandingEyeHeight * 0.5));
    private int _thirdPersonMilliseconds;

    /// <inheritdoc/>
    public MouseLookSettings LookSettings
    {
        set
        {
            _death.LookSettings = value;
            _interaction.LookSettings = value;
        }
    }

    /// <inheritdoc/>
    public bool IsDeathViewActive => gameplay.World.IsPlayerDead && _death.IsActive;

    /// <inheritdoc/>
    public bool IsInteractionViewActive => _interaction.IsActive;

    /// <inheritdoc/>
    public bool ShowsViewModel => _thirdPerson.ShowsViewModel;

    /// <inheritdoc/>
    public bool ShowsBody => _thirdPerson.ShowsBody;

    /// <inheritdoc/>
    public bool IsThirdPersonEngaged => _thirdPerson.IsEngaged;

    /// <inheritdoc/>
    public bool HasPlayer => gameplay.World.Player is not null;

    /// <inheritdoc/>
    public Vector3d? BodyPositionUnits => gameplay.World.Player?.Body.Position;

    /// <inheritdoc/>
    public Vector3d? EyeUnits => gameplay.World.Player?.Body.Eye;

    /// <inheritdoc/>
    public IReadOnlyList<string>? DebugLines()
    {
        if (gameplay.World.Player is not { } player)
        {
            return null;
        }

        Vector3d pu = player.PositionUnits;
        Vector3d pm = player.Position;
        return
        [
            "mode: WALK",
            FormattableString.Invariant($"pos units ({pu.X:F0}, {pu.Y:F0}, {pu.Z:F0})"),
            FormattableString.Invariant($"pos m     ({pm.X:F2}, {pm.Y:F2}, {pm.Z:F2})"),
            FormattableString.Invariant($"yaw {player.Yaw * 180f / MathF.PI:F1}  pitch {player.Pitch * 180f / MathF.PI:F1}"),
            FormattableString.Invariant($"grounded {player.IsGrounded}  crouch {player.IsCrouched}  wall {player.TouchedWall}"),
        ];
    }

    /// <inheritdoc/>
    public WorldRayCast? GroundTrace => gameplay.World.Player?.GrenadeWalls;

    /// <inheritdoc/>
    public bool IsBinocularsOpen => gameplay.World.Player is { IsBinocularsOpen: true };

    /// <inheritdoc/>
    public bool HasPlayerHud => gameplay.World.Player is { View: not null, IsMountedStationaryGun: false };

    /// <inheritdoc/>
    public bool HasFirstPersonHud => gameplay.World.Player is { IsOnLadder: false, IsInWorldInteraction: false };

    /// <inheritdoc/>
    public SightZoomFrame? SightZoom(float tickBlend) => gameplay.World.Player?.SmoothedSightZoom(tickBlend);

    /// <inheritdoc/>
    public float BinocularZoomIn(float tanX) => gameplay.World.Player?.BinocularZoomInFraction(tanX) ?? 0f;

    /// <inheritdoc/>
    public InteractionPromptTarget? InteractionPrompt(float tickBlend) => gameplay.World.Player?.InteractionPrompt(tickBlend);

    /// <inheritdoc/>
    public Camera3D? WalkingEye(bool smooth, float tickBlend)
    {
        if (gameplay.World.Player is not { IsOnLadder: false, IsInWorldInteraction: false } player)
        {
            return null;
        }

        Camera3D eye = smooth && player.HasViewTickPair ? player.SmoothedCamera(tickBlend) : player.Camera;

        // A co-op client's correction from the server moved the body at once; the eye catches up over a tenth of a second.
        Vector3d eased = player.ViewError.Offset(tickBlend);
        return eased == default ? eye : new Camera3D(eye.Position + eased, eye.Orientation);
    }

    /// <inheritdoc/>
    public void Renew()
    {
        // The camera the game hands the view to when the player dies -- it pulls back off the first-person eye to a
        // third-person shot of the body (HumanCamera death mode).
        _death = new();
        _interaction = new();
    }

    /// <inheritdoc/>
    public void ControlDeathView(float deltaX, float deltaY) => _death.Control(deltaX, deltaY);

    /// <inheritdoc/>
    public void ControlInteractionView(float deltaX, float deltaY) => _interaction.Control(deltaX, deltaY);

    /// <inheritdoc/>
    public void BeginInteractionView(Vector3d feet, float facing, WorldRayCast? walls)
    {
        _interactionFeet = feet;
        _interaction.Begin(EyeOver(feet), feet, facing, walls, death: false);
    }

    /// <inheritdoc/>
    public void FollowInteractionView(Vector3d feet) => _interactionFeet = feet;

    /// <inheritdoc/>
    public void TickInteractionView() => _interaction.Tick(EyeOver(_interactionFeet), _interactionFeet);

    /// <inheritdoc/>
    public void ResetInteractionView() => _interaction.Reset();

    /// <inheritdoc/>
    public void BeginDeathView(Vector3d position, float facing, WorldRayCast walls)
    {
        ArgumentNullException.ThrowIfNull(walls);
        _death.Begin(EyeOver(position), position, facing, walls, death: true);
    }

    /// <inheritdoc/>
    public void FollowDeathView(Vector3d centre)
    {
        Vector3d feet = FeetUnder(centre);
        _death.Tick(EyeOver(feet), feet);
    }

    /// <inheritdoc/>
    public void ResetDeathView() => _death.Reset();

    /// <inheritdoc/>
    public Camera3D ComposeEye(in ViewpointFrame frame, out Camera3D bodyEye)
    {
        MissionWorld mission = gameplay.World;

        // A dead player watches from the death camera -- pulled back off the eye to a third-person
        // shot of the body -- rather than from the frozen first-person view.
        // On a ladder the eye pulls back to a third-person interaction view (camera mode 3), so you
        // see your character on the rungs rather than the back of your hands.
        // The ladder and world-interaction view is a third-person orbit whose ANGLES are already
        // this frame's own -- Control runs per frame -- but whose pivot follows the climbing body
        // and whose distance eases out, both on the 30 Hz tick. The body itself is drawn
        // interpolated between its tick pair, so leaving the camera on the tick made a smoothly
        // climbing Jones judder against a camera that stepped thirty times a second. This is the
        // other half of that smoothing, and it must run BEFORE the eye is read below.
        if (_interaction.IsActive)
        {
            _interaction.Present(frame.Smooth ? frame.TickBlend : 1f);
        }

        if (_death.IsActive)
        {
            _death.Present(frame.Smooth ? frame.TickBlend : 1f);
        }

        Camera3D eye = mission.IsPlayerDead && _death.IsActive
            ? _death.Camera
            : (mission.Player is { IsOnLadder: true } or { IsInWorldInteraction: true })
                && _interaction.IsActive
                ? _interaction.Camera
                : frame.PlayerMode && frame.TitleEye is { } titleEye ? titleEye
                : frame.PlayerMode && mission.Player is not null ? mission.Player.Camera : frame.FreeEye;

        // The walking eye glides between its tick pair, and so does the recoil kick inside its
        // orientation; the mouse half of the look is this frame's own. The fly and death cameras
        // move per frame on their own and need nothing.
        if (frame.Smooth
            && frame.PlayerMode
            && !mission.IsPlayerDead
            && mission.Player is { HasViewTickPair: true, IsOnLadder: false, IsInWorldInteraction: false })
        {
            eye = mission.Player.SmoothedCamera(frame.TickBlend);
        }

        // The body's own eye, before the over-the-shoulder view moves the camera off it: the
        // flashlight hangs off the body, wherever the camera is.
        bodyEye = eye;

        // The over-the-shoulder view, built off whatever camera the player would have had. It is
        // applied HERE, on the finished first-person eye and before anything reads the eye back,
        // so the capture log, the aim projection and every pass of the frame all agree on where
        // the picture was taken from.
        //
        // The four states that own the view outright are excluded rather than blended out of: a
        // cutscene, the entry shot, the map computer and the menu orbit each replace the eye,
        // and running the move underneath one of them would only decide where the camera was
        // when it was handed back. The PAUSE is not among them: a paused frame is still the
        // player's own view of the level they are standing in, so the camera stays where it was
        // and simply stops moving.
        bool thirdPersonEligible = frame.PlayerMode
            && !mission.IsPlayerDead
            && frame.ThirdPersonAllowed
            && mission.Player is { IsOnLadder: false, IsInWorldInteraction: false };

        if (thirdPersonEligible && mission.Player is { } thirdPersonPlayer)
        {
            float thirdPersonElapsed =
                Math.Clamp((frame.NowMilliseconds - _thirdPersonMilliseconds) / 1000f, 0f, 0.1f);
            _thirdPersonMilliseconds = frame.NowMilliseconds;

            _thirdPerson.DistanceInMetres = GameplayCVars.ThirdPersonDistance.Value;
            _thirdPerson.ShoulderInMetres = GameplayCVars.ThirdPersonShoulder.Value;
            _thirdPerson.HeightInMetres = GameplayCVars.ThirdPersonHeight.Value;
            _thirdPerson.BlendSeconds = GameplayCVars.ThirdPersonBlend.Value;

            // Optics are a first-person act. Raising the binoculars or leaning into the Dragunov's
            // scope replaces the picture with what the eye sees, so the camera comes back to the
            // eye first -- by the same move, not by a cut -- and goes out again on the way down.
            bool wantsThirdPerson = GameplayCVars.ThirdPerson.Value
                && thirdPersonPlayer is { IsBinocularsOpen: false, IsSniperScoped: false };

            // A paused view does not move. Advancing the blend behind the menu would have the
            // camera quietly travel while the player was reading, and arrive somewhere else.
            eye = _thirdPerson.Update(
                frame.Paused ? 0f : thirdPersonElapsed,
                wantsThirdPerson,
                eye,
                frame.Walls);

            ConvergeThirdPersonAim(thirdPersonPlayer, eye);
        }
        else
        {
            // A cut, not a move: the view was taken by something that put the camera somewhere
            // else entirely, and interpolating back from there draws a frame from a line between
            // two unrelated places.
            _thirdPerson.Reset();
            _thirdPersonMilliseconds = frame.NowMilliseconds;
            if (mission.Player is not null)
            {
                mission.Player.AimConvergenceRay = null;
            }
        }

        return eye;
    }

    /// <summary>Hands the player the ray their crosshair sits on.</summary>
    /// <remarks>
    /// <para>
    /// Only the ray. What it is over is settled on the simulation tick, by
    /// <see cref="PlayerCamera.AimConvergenceRay"/>, against the same world trace and the same human
    /// probe a round marches -- because a crosshair resolved by one test and a bullet resolved by
    /// another disagree exactly where it matters. The first attempt at this traced here, with the
    /// renderer's own wall query, and looked straight through people: every shot at a guard converged on
    /// whatever stood behind him and landed the camera's own half-metre wide.
    /// </para>
    /// <para>
    /// Cleared the moment the camera is back at the eye, which is what leaves first person firing along
    /// the body's forward with no branch anywhere to say so.
    /// </para>
    /// </remarks>
    private void ConvergeThirdPersonAim(PlayerCamera player, in Camera3D drawn)
    {
        if (!_thirdPerson.IsEngaged)
        {
            player.AimConvergenceRay = null;
            return;
        }

        player.AimConvergenceRay = (
            new Vector3d(
                drawn.Position.X * WorldScene.UnitsPerMetre,
                drawn.Position.Y * WorldScene.UnitsPerMetre,
                drawn.Position.Z * WorldScene.UnitsPerMetre),
            drawn.Orientation.Row3);
    }
}
