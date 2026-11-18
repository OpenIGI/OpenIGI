using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Standing a fallen player up again: what co-op does, which the original never needed.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>
    /// Stands the player up again, whole, where it is put and facing as it is told: a co-op partner revived, or the squad taken
    /// back to where it last stood together.
    /// </summary>
    /// <param name="at">Where its feet stand, in world units.</param>
    /// <param name="gamma">The heading it faces.</param>
    /// <remarks>
    /// The death's own work undone (<see cref="EnterDie"/>): the dead latch and signal, the locked action and look, the death
    /// camera; the damage cleared as a new player's is (<see cref="Create"/>), and the body stood in its first state. A player
    /// still alive is only healed.
    /// </remarks>
    public void Revive(Vector3d at, float gamma)
    {
        Damage.Clear(General.DamageMultipliers);
        Damage.LastPart = Igi2DamagePart.None;
        if (!IsDead)
        {
            return;
        }

        IsDead = false;
        Locomotion.Flags &= ~DeadSignal;
        Signal = Igi2PlayerSignal.None;
        IsLockAction = false;
        IsRotateUpperBody = true;
        IsLockOrientation = false;
        View.IsLockOrientation = false;
        CameraMode = 1;
        IsDeathAnimationDone = false;
        IsDeathPositionStabilized = false;
        Speed = default;
        TotalSpeed = default;
        ImpactSpeed = default;
        Position = at;
        Gamma = gamma;
        IdealGamma = gamma;
        float[] facing = OpenIGI.Game.EngineEuler.Rotation(0f, 0f, gamma);
        Orientation = new Matrix3f(facing[0], facing[1], facing[2], facing[3], facing[4], facing[5], facing[6], facing[7], facing[8]);
        Killer = null;
        Locomotion.Enter(Igi2PlayerState.Stand, this);

        // Kept through the fall (KeepsWeaponsWhenDown): the weapon is back in hand and ready, its clips the stand's.
        if (Weapon.Inventory?.Current is not null)
        {
            IsWeaponReady = true;
            GoAction(Igi2PlayerActionState.Idle);
        }

        MaintainCollisionZOffset();
        UpdateViewBody(updateAnimation: false);
    }

    /// <summary>
    /// Gets or sets whether dying leaves the inventory as it was, for a revival to hand back: a co-op session holds its dead
    /// to be revived (Project I.G.I.'s partners keep their kit the same way). Off, death empties it, as the original's does.
    /// </summary>
    public bool KeepsWeaponsWhenDown { get; set; }
}
