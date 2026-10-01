namespace OpenIGI.Game.Weapons;

/// <summary>How a close-combat strike books its damage.</summary>
/// <remarks>
/// <para>
/// A punch lands on <b>hit zone 13</b> — the front torso, the same zone a shot from the front
/// resolves to and the same one explosions and falls book to. Zones 12 through 14 all carry a 1.0
/// multiplier, so the choice is bookkeeping rather than arithmetic; 13 is the geometrically honest
/// one for a face-to-face strike inside 1.5 metres.
/// </para>
/// <para>
/// The damage itself is the attacker's row <c>CloseCombatDamage</c>, already gated by
/// <c>CloseCombatHitProbability</c> in <c>AiPatrolCombat.RollMeleeStrike</c>. Like a round's factor,
/// it flows through the standard accumulator, so the player's difficulty scale applies unchanged.
/// </para>
/// </remarks>
public static class MeleeStrike
{
    /// <summary>The hit zone every close-combat strike applies to.</summary>
    public const int Zone = 13;
}
