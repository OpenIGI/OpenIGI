using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What a bullet's trace reads of the level: the surfaces it strikes and what they are made of.</summary>
public interface IIgi2GunShotWorld
{
    /// <summary>Gets the lines a bullet is traced along, <c>World_TraceLines</c> (<c>igi2 0x551640</c>).</summary>
    IIgi2CollisionQuery Collision { get; }

    /// <summary>Gets the session's materials, which the rows a hit names index.</summary>
    Igi2GameMaterials Materials { get; }

    /// <summary>Gets whether what was struck is water, a type derived from <c>Water</c>.</summary>
    /// <param name="struck">What was struck.</param>
    /// <returns>Whether it is water.</returns>
    bool IsWater(IWorldCollider struck);

    /// <summary>Gets whether what was struck is a boned object, a type derived from <c>BoneDynCubeObj</c>: a body.</summary>
    /// <param name="struck">What was struck.</param>
    /// <returns>Whether it is a boned object.</returns>
    bool IsBoned(IWorldCollider struck);

    /// <summary>Gets whether what was struck is a human, a type derived from <c>Human</c>.</summary>
    /// <param name="struck">What was struck.</param>
    /// <returns>Whether it is a human.</returns>
    bool IsHuman(IWorldCollider struck);

    /// <summary>
    /// Lets every human weigh a shot's bullets before they are traced, <c>Human_eEvaluateGunShotQTaskEvent</c> sent over
    /// <c>Human_ptQTaskList</c> by <c>GunShot_RunHandler</c>: a soldier that hears one go by is told of it.
    /// </summary>
    /// <param name="shot">The shot, its bullets not yet traced.</param>
    /// <param name="shooter">Who made the shot, <c>ptGunShotCreatorQTaskRef</c>.</param>
    void EvaluateGunShot(Igi2GunShot shot, object shooter)
    {
    }
}

/// <summary>
/// What a weapon's hit hands the object struck, <c>GameFunctions_eWeaponHitQTaskEvent</c>'s context as
/// <c>GunShot_RunInstant</c> (<c>igi2 0x4C6E85</c>) fills it.
/// </summary>
/// <param name="Intersection">Where the bullet struck: the face, the point and the materials either side.</param>
/// <param name="Damage">How much damage the bullet still does there: its damage factor, less up to 30% over its range, times what is left of its penetration.</param>
/// <param name="ShotVector">The shot's line: a bullet's from where this leg of its trace started to its end, in world units; a stab's unit direction.</param>
/// <param name="Shooter">The human whose weapon fired it, or <see langword="null"/>.</param>
/// <param name="WeaponType">The weapon that fired it.</param>
/// <param name="AmmoType">The ammunition, or <see langword="null"/>.</param>
public readonly record struct Igi2WeaponHit(
    WorldIntersection Intersection,
    float Damage,
    Vector3f ShotVector,
    IIgi2WeaponOwner? Shooter,
    Igi2WeaponTypeTask WeaponType,
    Igi2AmmoTypeTask? AmmoType)
{
    /// <summary>The hit as either game's targets read it: its fields, and the whole of it as the context.</summary>
    /// <returns>The shared hit.</returns>
    public WeaponHit ToShared() => new(
        Intersection.Position, Intersection.Normal, Damage, ShotVector, Shooter, this, (WeaponCategory)WeaponType.WeaponType, Intersection.Sector);

    /// <summary>Another game's hit in this game's terms, as its targets read it.</summary>
    /// <param name="hit">The hit.</param>
    /// <returns>The hit.</returns>
    /// <remarks>
    /// The struck point and bone make the intersection, and a weapon of the hit's kind stands for the weapon, which this game's
    /// data does not name: both engines number a weapon's type alike. A shooter of the other game is none of this game's, and
    /// no ammunition is counted toward accuracy. The damage is the other game's, as it sent it.
    /// </remarks>
    public static Igi2WeaponHit FromShared(in WeaponHit hit) => new(
        new WorldIntersection { Position = hit.Position, Normal = hit.Normal, Sector = hit.Bone, IsHit = true },
        hit.Damage,
        hit.ShotVector,
        hit.Shooter as IIgi2WeaponOwner,
        new Igi2WeaponTypeTask { WeaponType = (int)hit.Category },
        null);
}

/// <summary>An object that answers a bullet striking it, a type with a <c>GameFunctions_eWeaponHitQTaskEvent</c> handler.</summary>
/// <remarks>
/// The engine's <see cref="IWeaponHitTarget"/>, read in this game's terms: a hit this game sent is taken whole, and another
/// game's as <see cref="Igi2WeaponHit.FromShared"/> reads it.
/// </remarks>
public interface IIgi2WeaponHitTarget : IWeaponHitTarget
{
    /// <summary>Takes a bullet's hit.</summary>
    /// <param name="hit">The hit.</param>
    void WeaponHit(in Igi2WeaponHit hit);

    /// <inheritdoc/>
    void IWeaponHitTarget.WeaponHit(in WeaponHit hit)
    {
        WeaponHit(hit.Context is Igi2WeaponHit own ? own : Igi2WeaponHit.FromShared(hit));
    }
}

