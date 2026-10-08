using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// What a weapon's grenade is thrown with and goes off with, the <c>Grenade</c> projectile's parameters
/// (<c>Grenade_CreateWeaponTypeParametersHandler</c>, <c>igi2 0x484A20</c>): tagged values on the fire type that throws it.
/// </summary>
/// <param name="ExplosionType">The <c>Explosion Type</c>, <c>+0x28</c>; zero for one not tagged.</param>
/// <param name="ThrowIdealAngleDegrees">The <c>Throw Ideal Angle (degrees)</c>, <c>+0x2C</c>; 120 untagged.</param>
/// <param name="ThrowGammaExponent">The <c>Throw Gamma Exponent</c>, <c>+0x30</c>; one untagged.</param>
/// <param name="ExplosionDamageFactor">The <c>Explosion Damage Factor</c>, <c>+0x34</c>; one untagged.</param>
/// <param name="ExplosionRadiusMeters">The <c>Explosion Radius (meters)</c>, <c>+0x38</c>; five untagged.</param>
/// <param name="ExplosionFalloffMeters">The <c>Explosion Falloff (meters)</c>, <c>+0x3C</c>; none untagged.</param>
public readonly record struct Igi2GrenadeParameters(
    int ExplosionType,
    float ThrowIdealAngleDegrees,
    float ThrowGammaExponent,
    float ExplosionDamageFactor,
    float ExplosionRadiusMeters,
    float ExplosionFalloffMeters)
{
    /// <summary>Reads a fire type's grenade parameters from its tagged values, <c>TagItem_Find</c> by name.</summary>
    /// <param name="fireType">The fire type, whose children carry the tags.</param>
    /// <returns>The parameters, each untagged one at the handler's default.</returns>
    public static Igi2GrenadeParameters Of(GameTask fireType)
    {
        ArgumentNullException.ThrowIfNull(fireType);
        int type = 0;
        float ideal = 120f, exponent = 1f, damage = 1f, radius = 5f, falloff = 0f;
        foreach (GameTask child in fireType.Children)
        {
            switch (child)
            {
                case Igi2TagItemInt32Task { TagName: "Explosion Type" } tag:
                    type = tag.Value;
                    break;
                case Igi2TagItemReal32Task { TagName: "Throw Ideal Angle (degrees)" } tag:
                    ideal = tag.Value;
                    break;
                case Igi2TagItemReal32Task { TagName: "Throw Gamma Exponent" } tag:
                    exponent = tag.Value;
                    break;
                case Igi2TagItemReal32Task { TagName: "Explosion Damage Factor" } tag:
                    damage = tag.Value;
                    break;
                case Igi2TagItemReal32Task { TagName: "Explosion Radius (meters)" } tag:
                    radius = tag.Value;
                    break;
                case Igi2TagItemReal32Task { TagName: "Explosion Falloff (meters)" } tag:
                    falloff = tag.Value;
                    break;
            }
        }

        return new Igi2GrenadeParameters(type, ideal, exponent, damage, radius, falloff);
    }
}

/// <summary>
/// A grenade as it leaves the hand, what <c>Grenade_CreateProjectileHandler</c> (<c>igi2 0x484BE0</c>) passes
/// <c>Grenade_New</c> (<c>0x484FD0</c>).
/// </summary>
/// <param name="Position">Where it starts.</param>
/// <param name="Orientation">Its orientation, the fire's: nine floats row by row.</param>
/// <param name="Speed">Its speed, world units a tick.</param>
/// <param name="ExplodeTime">The ticks before it goes off.</param>
/// <param name="WeaponType">The weapon it is, whose <c>Fixed Model</c> names its physics type.</param>
/// <param name="Parameters">What it goes off with.</param>
/// <param name="Thrower">Who threw it, or <see langword="null"/>.</param>
public readonly record struct Igi2GrenadeThrow(
    Vector3d Position,
    float[] Orientation,
    Vector3f Speed,
    int ExplodeTime,
    Igi2WeaponTypeTask WeaponType,
    Igi2GrenadeParameters Parameters,
    IIgi2WeaponOwner? Thrower)
{
    private const float HalfPi = 1.5707964f;
    private const float Pi = 3.1415927f;
    private const float ThreeHalvesPi = 4.712389f;
    private const float TwoPi = 6.2831855f;
    private const float InversePi = 0.31830987f;

    /// <summary>
    /// Works out the throw from a launch, <c>Grenade_CreateProjectileHandler</c> (<c>igi2 0x484BE0</c>), with the client's
    /// FPU arithmetic.
    /// </summary>
    /// <param name="launch">The launch: the fire's position and orientation, the fire type's speed and the fuse.</param>
    /// <param name="parameters">The grenade's parameters.</param>
    /// <returns>The throw.</returns>
    /// <remarks>
    /// <para>
    /// The throw's strength follows how high the fire points. Its first angle is folded onto the upper half turn, measured
    /// from straight down in half turns, and held between none and the ideal angle (in half turns too); its share of the
    /// ideal, raised to the gamma exponent, scales the speed, which becomes world units a tick. The grenade flies along the
    /// fire's forward column at that speed, plus the thrower's own.
    /// </para>
    /// <para>
    /// The power is taken at double precision and then cut to the FPU's 24 bits: exact for the shipped exponent of one.
    /// </para>
    /// </remarks>
    public static Igi2GrenadeThrow Of(in Igi2ProjectileLaunch launch, in Igi2GrenadeParameters parameters)
    {
        float[] m = launch.Orientation;
        float ideal = ((Fpu)parameters.ThrowIdealAngleDegrees * 0.0055555557f).F;
        Fpu a = Igi2Pose.Angles(m).Alpha;
        if (a.Value >= HalfPi && a.Value < Pi)
        {
            a = (Fpu)Pi - a;
        }

        if (a.Value >= Pi && a.Value < ThreeHalvesPi)
        {
            a = (Fpu)TwoPi - (a - Pi);
        }

        if (a.Value <= HalfPi)
        {
            a += TwoPi;
        }

        Fpu height = (a - ThreeHalvesPi) * InversePi;
        float share = height.Value < 0.0 ? 0f : ideal < height.Value ? ideal : height.F;
        Fpu ratio = (Fpu)share / ideal;
        Fpu power = new(X87Single.Chop(Math.Pow(ratio.Value, parameters.ThrowGammaExponent)));
        Fpu speed = ((power * launch.Speed) * 4096f) * 0.033333335f;

        float sx = ((Fpu)m[1] * speed).F;
        Fpu sy = (Fpu)m[4] * speed;
        float sz = ((Fpu)m[7] * speed).F;
        Vector3f velocity = new(
            ((Fpu)sx + launch.ParentSpeed.X).F,
            (sy + launch.ParentSpeed.Y).F,
            ((Fpu)sz + launch.ParentSpeed.Z).F);
        return new Igi2GrenadeThrow(
            launch.Position,
            [.. m],
            velocity,
            launch.ExplodeTime < 0 ? 0 : launch.ExplodeTime,
            launch.WeaponType,
            parameters,
            launch.Thrower);
    }
}

/// <summary>Where the grenades thrown go: the level they fly through until they go off.</summary>
public interface IIgi2GrenadeWorld
{
    /// <summary>A grenade leaves the hand, <c>Grenade_New</c> (<c>igi2 0x484FD0</c>).</summary>
    /// <param name="grenade">The throw.</param>
    void Launch(in Igi2GrenadeThrow grenade);
}

/// <summary>A level with no grenade in it: a grenade thrown into it is gone.</summary>
public sealed class Igi2NoGrenades : IIgi2GrenadeWorld
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2NoGrenades Instance { get; } = new();

    /// <inheritdoc/>
    public void Launch(in Igi2GrenadeThrow grenade)
    {
    }
}
