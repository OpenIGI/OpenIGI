using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>Project I.G.I.'s own <c>WeaponHit</c> context, as its soldiers' handler reads it.</summary>
/// <param name="Hit">Where the round met the body: the point, the zone and the bone.</param>
/// <param name="Origin">Where the round started.</param>
/// <param name="End">Where it was cast to.</param>
/// <param name="DamageFactor">The weapon's damage.</param>
/// <param name="Category">What kind of weapon it is.</param>
/// <param name="Shooter">The soldier who fired it, or <see langword="null"/> for the player or a gun nobody holds.</param>
/// <param name="FromPlayer">Whether the player fired it.</param>
public sealed record Igi1WeaponHit(
    HumanHit Hit,
    Vector3d Origin,
    Vector3d End,
    float DamageFactor,
    WeaponCategory Category,
    SoldierView? Shooter,
    bool FromPlayer)
{
    /// <summary>The hit as either game's targets read it.</summary>
    /// <returns>The shared hit, this as its context.</returns>
    public WeaponHit ToShared()
    {
        Vector3d line = End - Origin;
        return new WeaponHit(
            Hit.Position,
            default,
            DamageFactor,
            new Vector3f((float)line.X, (float)line.Y, (float)line.Z),
            Shooter?.Soldier,
            this);
    }
}

/// <summary>Project I.G.I.'s own <c>ExplosionHit</c> context, as its soldiers' handler reads it.</summary>
/// <param name="Position">Where the blast went off.</param>
/// <param name="RadiusUnits">How far its full damage reaches.</param>
/// <param name="FalloffUnits">How far past that it fades.</param>
/// <param name="DamageFactor">Its damage.</param>
/// <param name="KilledBy">The weapon category a death it causes is reported under.</param>
public sealed record Igi1ExplosionHit(
    Vector3d Position,
    float RadiusUnits,
    float FalloffUnits,
    float DamageFactor,
    WeaponCategory KilledBy = WeaponCategory.Grenade)
{
    /// <summary>The blast as either game's targets read it.</summary>
    /// <returns>The shared explosion, this as its context.</returns>
    public ExplosionHit ToShared() => new(Position, -1, DamageFactor, RadiusUnits, FalloffUnits, null, this);
}
