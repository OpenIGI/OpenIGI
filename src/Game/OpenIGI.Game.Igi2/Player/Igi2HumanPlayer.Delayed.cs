using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>What the player settles once everything else has run in a tick.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The most the carried weapons can slow the player: to half speed.</summary>
    public const float MaxMovementSlowDown = 0.5f;

    /// <summary>How fast the posture's precision settles each tick, <c>0x3F333333</c>.</summary>
    private const float PrecisionSettling = 0.7f;

    /// <summary>Gets the precision the posture has settled to, <c>vActualModifiedPrecision</c>.</summary>
    public float ActualModifiedPrecision { get; private set; }

    /// <summary>Gets or sets the precision the weapon aims for, <c>vIdealWeaponPrecision</c>; firing spreads it.</summary>
    public float IdealWeaponPrecision { get; set; }

    /// <summary>Gets the weapon's precision as it has settled, <c>vActualWeaponPrecision</c>.</summary>
    public float ActualWeaponPrecision { get; private set; }

    /// <summary>Gets or sets how fast the weapon's precision settles, <c>vCrossHairDamping</c>; one when the player is made.</summary>
    public float CrossHairDamping { get; set; } = 1f;

    /// <summary>
    /// Gets a value indicating whether the player may zoom now, <c>HumanPlayer_IsZoomedModeAllowed</c> (<c>igi2 0x4B8890</c>):
    /// standing, crouching, lying or in one of the other still or slow postures retail lists, or in posture <c>0x50</c> with
    /// no move key held; and idle, starting, firing or letting go a shot, or firing empty.
    /// </summary>
    public bool IsZoomedModeAllowed
    {
        get
        {
            bool posture = Locomotion.CurrentState switch
            {
                0 or 1 or (>= 0xB and <= 0x12) or 0x17 or 0x18 or (>= 0x1A and <= 0x21) or 0x4B => true,
                0x50 => Channels[Igi2HumanChannel.MoveLeft] == 0f && Channels[Igi2HumanChannel.MoveRight] == 0f
                    && Channels[Igi2HumanChannel.MoveForwards] == 0f && Channels[Igi2HumanChannel.MoveBackwards] == 0f,
                _ => false,
            };
            int action = Action.CurrentState;
            return posture && action > 0 && (action <= Igi2PlayerActionState.FireRelease || action == Igi2PlayerActionState.FireEmpty);
        }
    }

    /// <summary>Gets the health the mission score shows, <c>tMissionScoreData.nHealth</c>: the percentage left, 0 to 100.</summary>
    public int MissionHealth { get; private set; } = 100;

    /// <summary>
    /// The living player's end of the tick: the alive half of <c>HumanPlayer_RunDelayedHandler</c> (<c>igi2 0x4B8A50</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The carried weapons slow the legs by up to half, and to half again while the jump wait runs. The posture's
    /// precision settles seven tenths of the way to its aim and the weapon's by the cross-hair damping. A zoom that may
    /// not be used now is left. The mission score's health is what is left of the maximum, in whole percent. The
    /// perfect-shot timer counts on, and the collision body is fitted to the posture. Then the body's animation
    /// controllers that step themselves do, and last the weapon's own end of the tick runs
    /// (<see cref="Weapons.Igi2WeaponFiring.RunDelayed"/>).
    /// </para>
    /// <para>
    /// Not ported yet: the scope's breathing sway (<c>HumanPlayer_HandleBreathViewOffset</c>, <c>igi2 0x4B4710</c>, a
    /// <c>Zoom</c> weapon's), the AI's view of the player, the save and load slots, the cheat keys, the inventory toggle,
    /// the running and fatigue sounds (<c>0x4BF140</c>, <c>0x4BF1C0</c>), the action menu and the sector lookup.
    /// </para>
    /// </remarks>
    public void RunDelayed()
    {
        float slowDown = Weapon.SummedMovementSlowDown;
        Fpu scale = (Fpu)1.0 - (slowDown < MaxMovementSlowDown ? slowDown : MaxMovementSlowDown);
        MovementSpeedScale = JumpWaitTimer != 0 ? (scale * 0.5).F : scale.F;

        ActualModifiedPrecision = ((((Fpu)IdealModifiedPrecision - ActualModifiedPrecision) * PrecisionSettling) + ActualModifiedPrecision).F;
        ActualWeaponPrecision = ((((Fpu)IdealWeaponPrecision - ActualWeaponPrecision) * CrossHairDamping) + ActualWeaponPrecision).F;

        if (!IsZoomedModeAllowed)
        {
            Weapon.LeaveZoomAlternateFireMode();
        }

        int lost = Fpu.ToInt32(((Fpu)Damage.Damage / Damage.MaxDamage) * 100.0);
        MissionHealth = Math.Clamp(100 - lost, 0, 100);
        PerfectShotTimer++;
        MaintainCollisionZOffset();

        // Then the body's children run (Human_RunHandler, igi2 0x4FE3A0, through BoneDynCubeObj_RunHandler, 0x565490):
        // the controllers that step themselves step, before the next tick's input runs the update.
        Animation.AutoStep();

        // The weapon's run handler queued its own delayed handler as the children ran, so it runs after this one.
        Weapon.RunDelayed();
    }
}
