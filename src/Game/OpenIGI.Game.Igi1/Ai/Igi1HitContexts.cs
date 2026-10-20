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
            this,
            Category,
            Hit.Bone);
    }

    /// <summary>Another game's round in this game's terms, met where it struck.</summary>
    /// <param name="hit">The round.</param>
    /// <param name="struck">Where it met the body, as this game casts it: the point, the zone and the bone.</param>
    /// <returns>The round: its line ending where it struck, its damage as the other game sent it, fired by none of this game's.</returns>
    public static Igi1WeaponHit FromShared(in WeaponHit hit, HumanHit struck)
    {
        Vector3d shot = new(hit.ShotVector.X, hit.ShotVector.Y, hit.ShotVector.Z);
        return new Igi1WeaponHit(struck, hit.Position - shot, hit.Position, hit.Damage, hit.Category, Shooter: null, FromPlayer: false);
    }

    /// <summary>
    /// Where another game's round meets a body of this game's: its line cast at the body stood at <paramref name="feet"/>, or the
    /// point it struck in the body's default zone when the cast misses.
    /// </summary>
    /// <param name="hit">The round.</param>
    /// <param name="feet">Where the body stands.</param>
    /// <param name="facing">Which way it faces, its local Y.</param>
    /// <returns>Where the round met it.</returns>
    public static HumanHit Meet(in WeaponHit hit, Vector3d feet, Vector3f facing)
    {
        Vector3d shot = new(hit.ShotVector.X, hit.ShotVector.Y, hit.ShotVector.Z);
        double length = shot.Length;
        if (length > 1e-6)
        {
            // Past the struck point by a body's depth, so the line crosses the whole of it.
            Vector3d end = hit.Position + (shot / length * HumanHitTest.BodyHeightInUnits);
            if (HumanHitTest.TryCast(hit.Position - shot, end, feet, facing, pose: null, out HumanHit cast))
            {
                return cast;
            }
        }

        return new HumanHit(hit.Position, length, HumanDamage.DefaultZone, 0f, 0f);
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

    /// <summary>Another game's blast in this game's terms.</summary>
    /// <param name="hit">The blast.</param>
    /// <returns>The blast: its full range the radius, its falloff, its damage as the other game sent it, a grenade's death.</returns>
    public static Igi1ExplosionHit FromShared(in ExplosionHit hit) =>
        new(hit.Position, hit.FullRange, hit.FalloffRange, hit.Damage);
}
