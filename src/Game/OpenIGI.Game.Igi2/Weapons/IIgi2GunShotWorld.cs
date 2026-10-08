using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

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
    bool IsWater(IIgi2Collider struck);

    /// <summary>Gets whether what was struck is a boned object, a type derived from <c>BoneDynCubeObj</c>: a body.</summary>
    /// <param name="struck">What was struck.</param>
    /// <returns>Whether it is a boned object.</returns>
    bool IsBoned(IIgi2Collider struck);
}

/// <summary>
/// What a weapon's hit hands the object struck, <c>GameFunctions_eWeaponHitQTaskEvent</c>'s context as
/// <c>GunShot_RunInstant</c> (<c>igi2 0x4C6E85</c>) fills it.
/// </summary>
/// <param name="Intersection">Where the bullet struck: the face, the point and the materials either side.</param>
/// <param name="Damage">How much damage the bullet still does there: its damage factor, less up to 30% over its range, times what is left of its penetration.</param>
/// <param name="ShotVector">From where this leg of the trace started to the bullet's end, in world units.</param>
/// <param name="Shooter">The human whose weapon fired it, or <see langword="null"/>.</param>
/// <param name="WeaponType">The weapon that fired it.</param>
/// <param name="AmmoType">The ammunition, or <see langword="null"/>.</param>
public readonly record struct Igi2WeaponHit(
    Igi2Intersection Intersection,
    float Damage,
    Vector3f ShotVector,
    IIgi2WeaponOwner? Shooter,
    Igi2WeaponTypeTask WeaponType,
    Igi2AmmoTypeTask? AmmoType);

/// <summary>An object that answers a bullet striking it, a type with a <c>GameFunctions_eWeaponHitQTaskEvent</c> handler.</summary>
public interface IIgi2WeaponHitTarget
{
    /// <summary>Takes a bullet's hit.</summary>
    /// <param name="hit">The hit.</param>
    void WeaponHit(in Igi2WeaponHit hit);
}

/// <summary>
/// What a weapon's trace hands an object asked whether the bullet may strike it,
/// <c>GameFunctions_eWeaponCollisionEvaluateQTaskEvent</c>'s context.
/// </summary>
/// <param name="Shooter">The human whose weapon fired, the weapon task's parent: an object never stops its own bullet.</param>
/// <param name="Ignore">What the shot passes through as though it were not there, <c>ptGunShotIgnoreQTaskRef</c>.</param>
/// <param name="HitProbability">How likely the bullet is to strike the player, the weapon's own (<c>Weapon+0x64</c>, 1 unless set).</param>
public readonly record struct Igi2WeaponCollisionQuery(object? Shooter, object? Ignore, float HitProbability);

/// <summary>An object that decides for itself whether a bullet may strike it, a type with a <c>GameFunctions_eWeaponCollisionEvaluateQTaskEvent</c> handler.</summary>
/// <remarks>A type with no handler takes every bullet.</remarks>
public interface IIgi2WeaponCollisionTarget
{
    /// <summary>Answers whether the bullet may strike this object.</summary>
    /// <param name="query">The trace's context.</param>
    /// <returns>Whether it may.</returns>
    bool EvaluateWeaponCollision(in Igi2WeaponCollisionQuery query);
}
