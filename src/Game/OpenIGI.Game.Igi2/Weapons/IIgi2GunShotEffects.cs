using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A tracer a bullet draws, <c>Tracerbullet_New</c> (<c>igi2 0x49DE10</c>) as <c>GunShot_RunInstant</c> asks for it.</summary>
/// <param name="Start">Where the bullet left from.</param>
/// <param name="End">Where it stopped: its last hit, or the end of its range.</param>
/// <param name="Shooter">The human whose weapon fired: the tracer leaves from its weapon object's second magic vertex when it has one.</param>
/// <param name="Texture">The weapon's <c>TracerTexture</c>.</param>
/// <param name="Velocity">The weapon's <c>TracerVelocity(m/s)</c>.</param>
public readonly record struct Igi2Tracer(Vector3d Start, Vector3d End, IIgi2WeaponOwner? Shooter, string Texture, float Velocity);

/// <summary>
/// The chips a bullet knocks off a surface: an <c>Explosion0</c> of no damage, <c>Explosion0_NewEx</c> (<c>igi2 0x50A650</c>)
/// as <c>GunShotEffect_CreateEffectHandler</c> (<c>0x520680</c>) asks for it.
/// </summary>
/// <param name="Position">Where the bullet struck.</param>
/// <param name="Fragments">How many fragments fly, one or two by a run-stream draw.</param>
/// <param name="Model">The fragments' model, the effect's <c>zModelName</c>.</param>
/// <param name="Direction">The direction they fly about: the bullet's reflection off the face, or its own direction out of a back face.</param>
/// <param name="Spin">The three angles the explosion draws for its fragments, each a run-stream draw over a tenth of a turn.</param>
public readonly record struct Igi2ImpactFragments(Vector3d Position, int Fragments, string Model, Vector3f Direction, (float A, float B, float C) Spin);

/// <summary>A splash a bullet makes on water, <c>Water_GunShotEffect</c> (<c>igi2 0x591FC0</c>).</summary>
/// <param name="Intersection">Where it struck the water.</param>
/// <param name="Penetration">What was left of the bullet's penetration.</param>
/// <param name="Effect">The effect the water names.</param>
public readonly record struct Igi2WaterSplash(WorldIntersection Intersection, float Penetration, Igi2GunShotEffectParameters Effect);

/// <summary>A hole a bullet leaves in what it struck, <c>BulletHole_eCreateBulletHoleTaskEvent</c>.</summary>
/// <param name="Struck">What was struck.</param>
/// <param name="Intersection">Where.</param>
/// <param name="Texture">The hole: the penetrating one where the bullet went on and the effect names one, the other otherwise.</param>
/// <param name="Size">The hole's size, the effect's <c>vBulletHoleSize</c>.</param>
public readonly record struct Igi2BulletHoleMark(IWorldCollider Struck, WorldIntersection Intersection, string Texture, float Size);

/// <summary>
/// The two puffs of smoke a bullet raises off a surface half the time, as <c>GunShotEffect_CreateEffectHandler</c>
/// (<c>igi2 0x520680</c>) adds them to a <c>Particle2DDynCubeObj</c>.
/// </summary>
/// <param name="Size">The puffs' size, the weapon's <c>Smoke Particle Size</c> in world units.</param>
/// <param name="First">Where the first puff starts: 120 cm off the face along its normal.</param>
/// <param name="Second">Where the second starts: a quarter of the size above the first.</param>
/// <param name="FirstRise">How fast the first rises.</param>
/// <param name="SecondRise">How fast the second rises, a quarter faster.</param>
/// <param name="Intensity">How strong both are.</param>
/// <param name="FirstSpin">How fast the first turns.</param>
/// <param name="SecondSpin">How fast the second turns.</param>
public readonly record struct Igi2ImpactSmoke(
    float Size,
    Vector3d First,
    Vector3d Second,
    float FirstRise,
    float SecondRise,
    float Intensity,
    float FirstSpin,
    float SecondSpin);

/// <summary>What a bullet's flight shows and sounds, behind which none of its logic depends.</summary>
/// <remarks>The one answer the logic reads back is whether a sound was made: a shot rings once, from its first hit that makes one.</remarks>
public interface IIgi2GunShotEffects
{
    /// <summary>Makes a sound at a point, <c>SoundSys_NewSoundFn</c> (<c>igi2 0x42F6F0</c>) then <c>SoundSys_eSetPositionQTaskEvent</c>.</summary>
    /// <param name="sound">The sound definition's name.</param>
    /// <param name="position">Where it sounds.</param>
    /// <returns>Whether a sound was made: there is none for an empty name or one no definition carries.</returns>
    bool NewSound(string sound, Vector3d position);

    /// <summary>Raises an event for the AI (<c>HumanAI_SendEvent</c>, <c>igi2 0x460A90</c>).</summary>
    /// <param name="aiEvent">The event.</param>
    void SendAiEvent(in Igi2WeaponAiEvent aiEvent);

    /// <summary>Draws a tracer.</summary>
    /// <param name="tracer">The tracer.</param>
    void Tracer(in Igi2Tracer tracer);

    /// <summary>Knocks chips off a surface.</summary>
    /// <param name="fragments">The chips.</param>
    void Fragments(in Igi2ImpactFragments fragments);

    /// <summary>Splashes water.</summary>
    /// <param name="splash">The splash.</param>
    void WaterSplash(in Igi2WaterSplash splash);

    /// <summary>Leaves a hole.</summary>
    /// <param name="hole">The hole.</param>
    void BulletHole(in Igi2BulletHoleMark hole);

    /// <summary>Raises smoke.</summary>
    /// <param name="smoke">The smoke.</param>
    void Smoke(in Igi2ImpactSmoke smoke);
}

/// <summary>Shows and sounds nothing, and makes no sound: what a bullet's flight does before its presentation is attached.</summary>
public sealed class Igi2NoGunShotEffects : IIgi2GunShotEffects
{
    /// <inheritdoc/>
    public bool NewSound(string sound, Vector3d position) => false;

    /// <inheritdoc/>
    public void SendAiEvent(in Igi2WeaponAiEvent aiEvent)
    {
    }

    /// <inheritdoc/>
    public void Tracer(in Igi2Tracer tracer)
    {
    }

    /// <inheritdoc/>
    public void Fragments(in Igi2ImpactFragments fragments)
    {
    }

    /// <inheritdoc/>
    public void WaterSplash(in Igi2WaterSplash splash)
    {
    }

    /// <inheritdoc/>
    public void BulletHole(in Igi2BulletHoleMark hole)
    {
    }

    /// <inheritdoc/>
    public void Smoke(in Igi2ImpactSmoke smoke)
    {
    }
}
