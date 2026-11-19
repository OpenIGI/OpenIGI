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

    /// <summary>Gets the eye the guards see the player's head at.</summary>
    /// <remarks>
    /// The original's peek moves only the camera, and a guard sees the body's eye whatever it does. Modern aim's lean and brace put
    /// the head where the camera is, so a head out round a corner or over a crate is a head a guard can see.
    /// </remarks>
    public Vector3d SeenEyeUnits => _firstPerson.ModernLean.Moves ? EyeUnits : _body.Eye;

    // A move, a jump or a change of stance lets go of a brace; letting go of the sights lets go of one they found.
    private bool MovingThisTick =>
        _input.IsSet(PlayerInputSlot.MoveUp) || _input.IsSet(PlayerInputSlot.MoveDown)
        || _input.IsSet(PlayerInputSlot.MoveLeft) || _input.IsSet(PlayerInputSlot.MoveRight)
        || _input.IsRisingEdge(PlayerInputSlot.Jump)
        || _input.Current.IsSet(PlayerInputSlot.Crouch) != _input.Previous.IsSet(PlayerInputSlot.Crouch);

    private void TickMount(AdsProfile? profile, bool canAim)
    {
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
                && MountProbe.Find(_body.Eye, _body.Yaw, walls) is { } surface)
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
