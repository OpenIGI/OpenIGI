using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>Where one projectile ended up.</summary>
/// <param name="Position">
/// The contact point in engine units, or the end of the round's range when it met nothing.
/// </param>
/// <param name="HitSomething">Whether a surface stopped it.</param>
/// <param name="RangeInUnits">How far it was allowed to travel, from the weapon's effective range.</param>
/// <remarks>
/// <para>
/// <b>This is one cast, not the original's loop.</b> <c>0x47B750</c> keeps casting while range is left,
/// penetrating and ricocheting through surfaces (<see cref="GunShotBallistics"/> has the rules); doing
/// that needs the per-surface resistance and graze limit, which the port's collision index does not
/// carry. What is faithful here is where the ray starts, which way it goes and how long it is, all
/// three of which come out of the weapon record with nothing chosen.
/// </para>
/// <para>See the original and the divergence in <c>TODO.md</c>.</para>
/// </remarks>
public readonly record struct GunImpact(Vector3d Position, bool HitSomething, float RangeInUnits);
