using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Weapons;

/// <summary>Where one projectile ended up.</summary>
/// <param name="Position">
/// The contact point in engine units, or the end of the round's range when it met nothing.
/// </param>
/// <param name="HitSomething">Whether a surface stopped it.</param>
/// <param name="RangeInUnits">How far it was allowed to travel, from the weapon's effective range.</param>
/// <param name="Owner">The exact placed task struck by a rich world trace, when available.</param>
/// <param name="Placement">The exact rendered placement struck, including an attached magic object.</param>
/// <param name="StruckHuman">Whether the contact was a human rather than a world surface.</param>
/// <param name="Wall">The world contact, when a surface stopped the shot.</param>
/// <param name="Direction">
/// Which way the round was travelling when it landed, unit length. The struck material's effect needs
/// it: <c>0x4083C0</c> packages the direction with the contact, and <c>0x438440</c> hands it on as the
/// axis the surface chips fly along.
/// </param>
/// <param name="Melee">
/// Whether a knife made the contact rather than a round. <c>KnifeStab</c> (<c>0x46DFB0</c>) only hands
/// the struck object its hit message: no <c>GunShotEffect</c>, so no mark, no sound, no dust.
/// </param>
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
public readonly record struct GunImpact(
    Vector3d Position,
    bool HitSomething,
    float RangeInUnits,
    GameTask? Owner = null,
    WorldPlacement? Placement = null,
    bool StruckHuman = false,
    WorldRayHit Wall = default,
    Vector3f Direction = default,
    bool Melee = false);
