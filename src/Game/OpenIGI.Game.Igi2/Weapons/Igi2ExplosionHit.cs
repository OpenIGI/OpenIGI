using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What an explosion tells each of its targets, <c>ExplosionHitContext_t</c>.</summary>
/// <param name="World">
/// The explosion world, which resolves what the blast does through what lies between; <see langword="null"/> for another
/// game's blast, which its own game has already weighed.
/// </param>
/// <param name="Position">Where it went off.</param>
/// <param name="Team">The team of the human who set it off, or -1.</param>
/// <param name="Damage">The damage at its heart.</param>
/// <param name="FullRange">How far the full damage reaches.</param>
/// <param name="FalloffRange">How far past that it falls away.</param>
/// <param name="ExplosionTime">The ticks its shake runs over, <c>nExplosionTime</c>: 90.</param>
/// <param name="WeaponType">The weapon it came from, or <see langword="null"/>.</param>
/// <param name="Owner">Who set it off, or <see langword="null"/>.</param>
/// <param name="Falloff">How its damage falls away.</param>
public readonly record struct Igi2ExplosionHit(
    Igi2ExplosionWorld? World,
    Vector3d Position,
    int Team,
    float Damage,
    float FullRange,
    float FalloffRange,
    int ExplosionTime,
    Igi2WeaponTypeTask? WeaponType,
    IIgi2WeaponOwner? Owner,
    Igi2ExplosionFalloff Falloff)
{
    /// <summary>The explosion as either game's targets read it: its fields, and the whole of it as the context.</summary>
    /// <returns>The shared explosion.</returns>
    public ExplosionHit ToShared() => new(Position, Team, Damage, FullRange, FalloffRange, Owner, this);

    /// <summary>Another game's blast in this game's terms, as its targets read it.</summary>
    /// <param name="hit">The blast.</param>
    /// <returns>The blast: no world to shield it, no weapon this game names, the engine's 90 ticks of shake and a straight falloff.</returns>
    public static Igi2ExplosionHit FromShared(in ExplosionHit hit) => new(
        null,
        hit.Position,
        hit.Team,
        hit.Damage,
        hit.FullRange,
        hit.FalloffRange,
        90,
        null,
        hit.Owner as IIgi2WeaponOwner,
        Igi2ExplosionFalloff.Linear);

    /// <summary>The damage it does a target, <c>GameFunctions_ResolveExplosionHit</c> (<c>igi2 0x48D1F0</c>).</summary>
    /// <param name="target">Where the target is, <c>tPos</c>.</param>
    /// <param name="radius">How big the target is.</param>
    /// <returns>The damage.</returns>
    public Fpu Resolve(Vector3d target, float radius) => Resolve(target, radius, null);

    /// <summary>The damage it does a target, as <see cref="Resolve(Vector3d, float)"/>, the target named so the line through walls passes it.</summary>
    /// <param name="target">Where the target is.</param>
    /// <param name="radius">The target's size.</param>
    /// <param name="self">The target, <c>ptQTask</c>.</param>
    /// <returns>The damage.</returns>
    public Fpu Resolve(Vector3d target, float radius, object? self) =>
        World is { } world ? world.Resolve(this, target, radius, self) : Igi2ExplosionWorld.Reach(this, target, radius);
}
