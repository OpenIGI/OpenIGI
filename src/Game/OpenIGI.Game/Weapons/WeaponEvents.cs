using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// What a bullet's trace hands an object asked whether the bullet may strike it: the engine's
/// <c>WeaponCollisionEvaluate</c> task event (Project I.G.I. <c>byte_539547</c>, I.G.I. 2
/// <c>GameFunctions_eWeaponCollisionEvaluateQTaskEvent</c>).
/// </summary>
/// <param name="Shooter">The human whose weapon fired, the weapon task's parent: an object never stops its own bullet.</param>
/// <param name="Ignore">What the shot passes through as though it were not there.</param>
/// <param name="HitProbability">How likely the bullet is to strike the player, the weapon's own (1 unless set).</param>
public readonly record struct WeaponCollisionQuery(object? Shooter, object? Ignore, float HitProbability);

/// <summary>An object that decides for itself whether a bullet may strike it: a type with a <c>WeaponCollisionEvaluate</c> handler.</summary>
/// <remarks>A type with no handler takes every bullet.</remarks>
public interface IWeaponCollisionTarget
{
    /// <summary>Answers whether the bullet may strike this object.</summary>
    /// <param name="query">The trace's context.</param>
    /// <returns>Whether it may.</returns>
    bool EvaluateWeaponCollision(in WeaponCollisionQuery query);
}

/// <summary>
/// What a weapon's hit hands the object struck: the engine's <c>WeaponHit</c> task event (Project I.G.I. <c>byte_539549</c>,
/// sent by the shot at <c>0x47BA54</c>; I.G.I. 2 <c>GameFunctions_eWeaponHitQTaskEvent</c>, sent by <c>GunShot_RunInstant</c>).
/// </summary>
/// <param name="Position">Where the bullet struck, in world units.</param>
/// <param name="Normal">The struck face's normal.</param>
/// <param name="Damage">How much damage the bullet still does there.</param>
/// <param name="ShotVector">The shot's line, its end less its start; a stab's unit direction.</param>
/// <param name="Shooter">Who fired it, or <see langword="null"/>.</param>
/// <param name="Context">
/// The sending game's own context, whole: both games fill the same six fields (the intersection, the damage, the shot
/// vector, the shooter, the weapon and the ammunition), in their own types. A target of the same game reads it; one of
/// another game reads the fields above.
/// </param>
public readonly record struct WeaponHit(
    Vector3d Position,
    Vector3f Normal,
    float Damage,
    Vector3f ShotVector,
    object? Shooter,
    object? Context);

/// <summary>An object that answers a bullet striking it: a type with a <c>WeaponHit</c> handler.</summary>
public interface IWeaponHitTarget
{
    /// <summary>Takes a bullet's hit.</summary>
    /// <param name="hit">The hit.</param>
    void WeaponHit(in WeaponHit hit);
}

/// <summary>
/// What an explosion tells each object it reaches: the engine's <c>ExplosionHit</c> task event (Project I.G.I.
/// <c>byte_53954B</c>; I.G.I. 2 <c>GameFunctions_eExplosionHitQTaskEvent</c>, <c>ExplosionHitContext_t</c>), sent over the
/// explosion list.
/// </summary>
/// <param name="Position">Where it went off.</param>
/// <param name="Team">The team of the human who set it off, or -1.</param>
/// <param name="Damage">The damage at its heart.</param>
/// <param name="FullRange">How far the full damage reaches, in world units.</param>
/// <param name="FalloffRange">How far past that it falls away.</param>
/// <param name="Owner">Who set it off, or <see langword="null"/>.</param>
/// <param name="Context">The sending game's own context, whole, as <see cref="WeaponHit.Context"/>.</param>
public readonly record struct ExplosionHit(
    Vector3d Position,
    int Team,
    float Damage,
    float FullRange,
    float FalloffRange,
    object? Owner,
    object? Context);

/// <summary>Something an explosion can hurt: a type with an <c>ExplosionHit</c> handler, a member of the explosion list.</summary>
public interface IExplosionHitTarget
{
    /// <summary>An explosion reaches it.</summary>
    /// <param name="hit">The explosion.</param>
    void ExplosionHit(in ExplosionHit hit);
}
