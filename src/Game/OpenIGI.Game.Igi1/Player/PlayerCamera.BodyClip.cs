using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>A whole-body clip a player plays -- a ladder's, a computer's, a lock's, a vault's -- as another player's window poses it.</summary>
/// <param name="Animation">The clip.</param>
/// <param name="Feet">Where the body stands, in world units: the place the clip is posed at.</param>
/// <param name="Facing">Which way the clip faces the body, a gamma (the look's yaw negated).</param>
/// <param name="Ladder">
/// Whether it is a ladder's clip, posed on the ladder with the clip's root height already in <paramref name="Feet"/>; any
/// other is posed as the player's own body poses that interaction's (<see cref="PlayerCamera.KeepsRootHeight"/>).
/// </param>
public readonly record struct PlayerBodyClip(int Animation, Vector3d Feet, float Facing, bool Ladder);

public sealed partial class PlayerCamera
{
    /// <summary>Gets whether the body plays a world interaction's own clip: any interaction but the mounted gun's sight.</summary>
    public bool PlaysInteractionClip => IsInWorldInteraction && !IsMountedStationaryGun && InteractionAnimation >= 0;

    /// <summary>Gets the ladder clip the climb is in, or -1 off a ladder: what another player's window draws it doing.</summary>
    public int LadderAnimation => !IsOnLadder
        ? -1
        : LadderPhase switch
        {
            LadderTraversalPhase.SlidingDown => LadderSlideAnimation,
            LadderTraversalPhase.GettingOnTop or LadderTraversalPhase.GettingOffTop => LadderTopAnimation,
            _ => LadderClimbAnimation,
        };

    /// <summary>
    /// Gets the whole-body clip the player plays, or <see langword="null"/> while it walks: a ladder's, else a world
    /// interaction's. What every other player's window draws it doing, the server's snapshot and a host's own partner alike.
    /// </summary>
    public PlayerBodyClip? BodyClip => LadderAnimation >= 0
        ? new PlayerBodyClip(LadderAnimation, LadderFeet, LadderFacing, Ladder: true)
        : PlaysInteractionClip
            ? new PlayerBodyClip(InteractionAnimation, InteractionFeet, InteractionFacing, Ladder: false)
            : ModeAnimation >= 0
                ? new PlayerBodyClip(ModeAnimation, Body.Position, -Body.Yaw, Ladder: false)
                : null;

    /// <summary>Gets the whole-body clip a multiplayer mode plays on the player (the bomb's plant or defuse), or -1 for none.</summary>
    public int ModeAnimation { get; private set; } = -1;

    /// <summary>Gets where the mode's clip is, in its own time (160 a tick).</summary>
    public float ModeClipTime { get; private set; }

    /// <summary>Gets where the body is in the clip it plays when it is the mode's, else <see langword="null"/>.</summary>
    public float? ModeClipTimeShown => ModeAnimation >= 0 && LadderAnimation < 0 && !PlaysInteractionClip ? ModeClipTime : null;

    /// <summary>Plays a mode's whole-body clip at a time, or stops it.</summary>
    /// <param name="animation">The clip, in the human set, or -1 for none.</param>
    /// <param name="time">Where in it, in its own time.</param>
    public void PlayModeClip(int animation, float time)
    {
        ModeAnimation = animation;
        ModeClipTime = animation >= 0 ? time : 0f;
    }

    // Whether the arms play the plant, so the weapon's own pose is put back on them when it ends.
    private bool _armsPlanting;

    /// <summary>Gets whether the player's hands are on the mode's bomb, planting or defusing it: its gun is put away.</summary>
    public bool HandsOnBomb => ModeAnimation is Multiplayer.Igi1BombAnimations.Plant or Multiplayer.Igi1BombAnimations.Defuse;

    /// <summary>Gets whether the bomb is the weapon in the player's hand.</summary>
    public bool HoldsBomb => _weapons?.InHand?.Weapon.WeaponId == Multiplayer.Igi1BombWeapon.Id;

    /// <summary>Gets the arms' clip and its time while they hold the bomb or plant it, for the charge they hold; else none.</summary>
    public (int Animation, float Time)? FirstPersonBombClip =>
        (HoldsBomb || _armsPlanting) && _firstPerson.Rig?.WeaponClip is { } clip ? (clip.Animation.Id, clip.Time) : null;

    // The weapon machine, and what it plays on the arms -- unless the hands are on the bomb, when the machine runs blind (no
    // clip of its own put on the arms) and the arms play the plant where the body's is.
    private void TickWeapons()
    {
        if (_weapons is null)
        {
            return;
        }

        HumanView? rig = _firstPerson.Rig;
        bool busy = HandsOnBomb && rig is not null;
        if (_weaponActions.Tick(_weapons, _input, busy ? null : rig, _body, LocomotionState))
        {
            PoseChanged = true;
        }

        if (rig is null)
        {
            return;
        }

        if (busy && ModeAnimation == Multiplayer.Igi1BombAnimations.Plant)
        {
            if (!_armsPlanting && rig.PlayWeaponAnimation(Multiplayer.Igi1BombAnimations.FirstPersonPlant, loops: false, blendSeconds: 0.15f) is null)
            {
                return;
            }

            _armsPlanting = true;
            rig.SkipTo(Multiplayer.Igi1BombAnimations.FirstPersonTime(ModeClipTime));
            PoseChanged = true;
        }
        else if (_armsPlanting)
        {
            _armsPlanting = false;
            if (_weapons.InHand?.Weapon is { } held)
            {
                _ = _weaponActions.Play(rig, held, OpenIGI.Game.Weapons.WeaponAction.Stand);
            }

            PoseChanged = true;
        }
    }

    /// <summary>Gets whether a clip is one of the ladder's three: the climb, the slide, the top.</summary>
    /// <param name="animation">The clip.</param>
    /// <returns><see langword="true"/> for 168, 169 and 170 in the player's set.</returns>
    public static bool IsLadderAnimation(int animation) =>
        animation == LadderClimbAnimation || animation == LadderSlideAnimation || animation == LadderTopAnimation;

    /// <summary>Gets whether an interaction clip lifts the body by its root height, as the walk's do.</summary>
    /// <param name="animation">The interaction's clip.</param>
    /// <returns>
    /// <see langword="true"/> for the computer, cabinet, lock and charge clips, whose task position is the feet;
    /// <see langword="false"/> for the fence's vault and the wires' rides, whose root motion carries the height already.
    /// </returns>
    public static bool KeepsRootHeight(int animation) =>
        animation != HumanLocomotionStates.For(41).AnimationId && !Ai.SoldierSimulation.IsWireAnimation(animation);
}
