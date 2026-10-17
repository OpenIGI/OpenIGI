using OpenIGI.Game.Weapons;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// What a soldier's held weapon looks like to its combat behaviour this tick.
/// </summary>
/// <param name="AmmoInClip">
/// How many rounds are left in the clip. The behaviour watches this <i>change</i> to know a round has
/// left — <c>HumanAI+15960</c> is last tick's value.
/// </param>
/// <param name="ClipSize">The weapon's clip size — its config <c>+1608</c>.</param>
/// <param name="BurstSize">
/// The weapon's own burst length — its config <c>+1612</c>. A value of one makes it single shot, which
/// is what puts the per-shot delay between rounds.
/// </param>
/// <param name="Category">The weapon's category; only a gun, a pistol or a shotgun fires in bursts.</param>
/// <remarks>
/// The weapon is <b>slot 0 of the human's list</b>, not whatever its <c>Slot</c> parameter says — every
/// consumer in the original calls <c>sub_4F2060(human + 1220, 0)</c>.
/// </remarks>
public readonly record struct AiCombatWeaponState(
    int AmmoInClip,
    int ClipSize,
    int BurstSize,
    WeaponCategory Category);
