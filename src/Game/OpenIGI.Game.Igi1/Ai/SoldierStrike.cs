using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// A round that struck a living soldier, as <see cref="SoldierSimulation.Struck"/> reports it: the facts of the hit, for a mod
/// to read what it means (where from, which side, how hard). <b>Not retail.</b>
/// </summary>
/// <param name="Soldier">The soldier struck.</param>
/// <param name="Hit">Where the round met him, the zone and the bone, in engine units.</param>
/// <param name="Origin">Where the round came from, in engine units.</param>
/// <param name="End">Where it was going, in engine units.</param>
/// <param name="Weapon">The weapon it came from.</param>
/// <param name="Damage">How much health it took; nought for a soldier who cannot be hurt.</param>
/// <param name="Killed">Whether it killed him.</param>
public sealed record SoldierStrike(SoldierView Soldier, HumanHit Hit, Vector3d Origin, Vector3d End, WeaponType Weapon, float Damage, bool Killed);
