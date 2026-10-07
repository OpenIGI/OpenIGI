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
    /// perfect-shot timer counts on, and the collision body is fitted to the posture.
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

        if (!Weapon.IsZoomedModeAllowed)
        {
            Weapon.LeaveZoomAlternateFireMode();
        }

        int lost = Fpu.ToInt32(((Fpu)Damage.Damage / Damage.MaxDamage) * 100.0);
        MissionHealth = Math.Clamp(100 - lost, 0, 100);
        PerfectShotTimer++;
        MaintainCollisionZOffset();
    }
}
