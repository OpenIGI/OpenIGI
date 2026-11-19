using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Player.Aim;

namespace OpenIGI.Game.Igi1.Player;

/// <content>
/// Modern aim's lean with the weapon up and its brace on ledges, tables and corners (<see cref="CombatLean"/>,
/// <see cref="WeaponMount"/>). <b>Not retail</b>; the original's lean is the peek, which this leaves alone.
/// </content>
public sealed partial class PlayerCamera
{
    /// <summary>What leaning out makes of the walk.</summary>
    public const float LeaningSpeedScale = 0.6f;

    // How many ticks an aim with nothing to brace on waits before it looks again.
    private const int MountProbeTicks = 4;

    private readonly CombatLean _lean = new();
    private readonly WeaponMount _mount = new();
    private bool _mountedByKey;
    private int _mountProbeWait;

    /// <summary>Gets the lean with the weapon up.</summary>
    public CombatLean CombatLean => _lean;

    /// <summary>Gets the weapon's brace.</summary>
    public WeaponMount Mount => _mount;

    /// <summary>Gets the sector a braced weapon holds the look to, or <see langword="null"/> for none.</summary>
    public MountLookLimit? LookLimit => _firstPerson.LookLimit;

    /// <summary>Gets the roll the body's torso leans out by, radians, positive to the right: modern aim's lean as others see it.</summary>
    public float BodyLeanRoll => _modernAim.Enabled ? CombatLean.Eye(_lean.Amount).Roll : 0f;

    /// <summary>Gets how far the weapon is up on its sights as others see it, nought to one, eased.</summary>
    public float BodyAim => _modernAim.Enabled ? AimDownSights.Shape(_ads.Progress) : 0f;

    /// <summary>The pose of the half of the body the legs' clip does not drive, as every window draws it.</summary>
    /// <returns>The torso's clip, the look, and modern aim's lean and sights.</returns>
    public Ai.PlayerBodyPose BodyPose() => new(
        UpperBodyAnimation,
        UpperBodyAnimationLoops,
        UpperBodyBlendSeconds,
        UpperBodySequence,
        AimPitch,
        BodyLeanRoll,
        BodyAim);

    /// <summary>Gets the eye the guards see the player's head at.</summary>
    /// <remarks>
    /// The original's peek moves only the camera, and a guard sees the body's eye whatever it does. Modern aim's lean and brace put
    /// the head where the camera is, so a head out round a corner or over a crate is a head a guard can see.
    /// </remarks>
    public Vector3d SeenEyeUnits => _firstPerson.ModernLean.Moves ? EyeUnits : _body.Eye;

    // How far a standing eye drops when the body crouches, in world units.
    private static float StandingCrouchDrop => (float)(PlayerBody.StandingEyeHeight - PlayerBody.CrouchingEyeHeight);

    // The drop a crouch would add to this body's eye now: nought once it is down.
    private float CrouchDrop => _body.IsCrouched ? 0f : StandingCrouchDrop;

    /// <summary>Gets a value indicating whether a brace on a low top holds the body crouched.</summary>
    public bool MountCrouches => _mount.IsMounted && _mount.Crouches;

    // A move, a jump or a change of stance lets go of a brace; letting go of the sights lets go of one they found.
    private bool MovingThisTick =>
        _input.IsSet(PlayerInputSlot.MoveUp) || _input.IsSet(PlayerInputSlot.MoveDown)
        || _input.IsSet(PlayerInputSlot.MoveLeft) || _input.IsSet(PlayerInputSlot.MoveRight)
        || _input.IsRisingEdge(PlayerInputSlot.Jump)
        || _input.Current.IsSet(PlayerInputSlot.Crouch) != _input.Previous.IsSet(PlayerInputSlot.Crouch);

    private void TickMount(AdsProfile? profile, bool canAim)
    {
        if (AdsTuningCVars.FindMount.Value)
        {
            AdsTuningCVars.FindMount.Value = false;
            StandAtNearestMount();
        }

        bool pressed = _modernAim.Enabled && _input.IsRisingEdge(PlayerInputSlot.Mount);
        bool moving = MovingThisTick;
        if (_mount.IsMounted)
        {
            bool letGo = !_modernAim.Enabled
                || moving
                || !ReferenceEquals(profile, _mount.Profile)
                || _weapons is { State: HumanWeaponState.ChangingWeapon or HumanWeaponState.Peeking }
                || (_mountedByKey ? pressed : !_ads.Wanted);
            if (letGo)
            {
                _mount.Dismount();
                _mountedByKey = false;
            }
        }
        else if (profile is { CanMount: true }
            && canAim
            && !moving
            && _body.IsGrounded
            && (pressed || (_modernAim.AutoMount && _ads.Wanted && (_mountProbeWait-- <= 0))))
        {
            _mountProbeWait = MountProbeTicks;
            if (PlayerWorldTraces.Eye(_walls, TerrainTrace) is { } walls
                && MountProbe.Find(_body.Eye, _body.Yaw, walls, CrouchDrop) is { } surface)
            {
                _mount.Mount(surface, profile, _body.Yaw);
                _mountedByKey = pressed;
            }
        }

        if (!_ads.Wanted && !pressed)
        {
            _mountProbeWait = 0;
        }

        _mount.Tick();
        _firstPerson.LookLimit = _mount.IsMounted ? _mount.Limit : null;
    }

    // g_ads_debug_findmount: rings of places round the player, each looked at in sixteen headings with the mount probe from
    // a standing eye over the ground found there; the nearest that offers a brace is where the player is stood, facing it.
    private void StandAtNearestMount()
    {
        if (PlayerWorldTraces.Eye(_walls, TerrainTrace) is not { } walls)
        {
            AdsLiveTuning.Found = "find mount: no world to search";
            return;
        }

        // A top first, the brace a crate or a sill gives, and a corner only where there is none.
        if (!StandAtNearestMount(walls, topsOnly: true) && !StandAtNearestMount(walls, topsOnly: false))
        {
            AdsLiveTuning.Found = "find mount: nothing to brace on within 40 m";
        }
    }

    private bool StandAtNearestMount(Game.World.WorldRayCast walls, bool topsOnly)
    {
        double unitsPerMetre = Game.World.WorldScene.UnitsPerMetre;
        Vector3d origin = _body.Position;
        for (double radius = 0.0; radius <= 40.0; radius += 0.5)
        {
            int around = radius == 0.0 ? 1 : (int)Math.Ceiling(radius * 2.0 * Math.PI / 0.5);
            for (int i = 0; i < around; i++)
            {
                double angle = i * 2.0 * Math.PI / around;
                Vector3d over = new(
                    origin.X + (Math.Sin(angle) * radius * unitsPerMetre),
                    origin.Y + (Math.Cos(angle) * radius * unitsPerMetre),
                    origin.Z + (3.0 * unitsPerMetre));
                if (!walls(over, over - new Vector3d(0.0, 0.0, 6.0 * unitsPerMetre), out Game.World.WorldRayHit ground)
                    || ground.Normal.Z < 0.8f)
                {
                    continue;
                }

                Vector3d feet = ground.Position;
                Vector3d eye = feet + new Vector3d(0.0, 0.0, PlayerBody.StandingEyeHeight);
                if (walls(feet + new Vector3d(0.0, 0.0, 0.1 * unitsPerMetre), eye, out _))
                {
                    continue;
                }

                for (int heading = 0; heading < 16; heading++)
                {
                    float yaw = heading * MathF.PI / 8f;
                    if (MountProbe.Find(eye, yaw, walls, StandingCrouchDrop) is { } surface && (!topsOnly || surface.Kind == MountKind.Top))
                    {
                        _body.Position = feet;
                        _body.Yaw = yaw;
                        _body.Pitch = 0f;
                        _firstPerson.Pair.HasPair = false;
                        AdsLiveTuning.Found = FormattableString.Invariant(
                            $"find mount: {surface.Kind}{(surface.Crouch ? " crouched" : string.Empty)} {radius:0.0} m away at ({feet.X / unitsPerMetre:0.00}, {feet.Y / unitsPerMetre:0.00}, {feet.Z / unitsPerMetre:0.00}) yaw {yaw:0.00} raise {surface.Raise / unitsPerMetre:+0.00;-0.00} m");
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void TickLean(bool free)
    {
        float target = 0f;
        if (_modernAim.Enabled && free)
        {
            target = CombatLean.Target(_input.IsSet(PlayerInputSlot.LeanLeft), _input.IsSet(PlayerInputSlot.LeanRight));
            if (target == 0f)
            {
                // At a corner the eye leans out into the open side unless the keys say otherwise.
                target = _mount.CornerSide;
            }
        }

        _lean.Tick(target);
        _firstPerson.ModernLean = _modernAim.Enabled ? CombatLean.Eye(_lean.Amount) + _mount.Eye : EyeLean.None;
    }
}
