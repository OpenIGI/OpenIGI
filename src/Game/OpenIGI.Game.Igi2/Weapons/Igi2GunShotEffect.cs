using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// What a bullet leaves on a face it crossed, <c>GunShotEffect_CreateEffectHandler</c> (<c>igi2 0x520680</c>): chips,
/// a splash, a hole and smoke, with the run-stream draws they take.
/// </summary>
/// <remarks>
/// <para>
/// A body, any boned object, gets neither chips nor smoke: its flesh is the human's own effect. Otherwise an effect that
/// throws chips draws once for one or two fragments and starts an <c>Explosion0</c> of no damage, which draws three
/// angles of its own (<c>Explosion0_NewEx</c>, <c>0x50A650</c>). Water then splashes and nothing more happens. Anything
/// else takes a hole where it has holes, the penetrating one if the bullet went on and the effect names one; and an
/// effect that smokes draws once more and, on an odd draw, raises two puffs from four further draws.
/// </para>
/// <para>
/// Not yet: the <c>Explosion0</c>'s own run, its fragments flying over the ticks after.
/// </para>
/// </remarks>
public static class Igi2GunShotEffect
{
    /// <summary>How far off the face the smoke starts, the float <c>491.52</c> (twelve centimetres) as a double.</summary>
    private const double SmokeOffset = 491.519989013671875;

    /// <summary>The fraction of a turn an explosion's fragment angles span, the float <c>0.62831855</c>.</summary>
    private const float FragmentSpan = 0.62831855f;

    /// <summary>Makes a face's effect.</summary>
    /// <param name="record">The face, as the trace recorded it.</param>
    /// <param name="weaponType">The weapon that fired: its <c>Smoke Particle Size</c> sizes the smoke.</param>
    /// <param name="world">The level, which says what was struck.</param>
    /// <param name="random">The run stream.</param>
    /// <param name="effects">What the effect shows.</param>
    public static void Create(Igi2GunShotHitRecord record, Igi2WeaponTypeTask weaponType, IIgi2GunShotWorld world, IgiRandom random, IIgi2GunShotEffects effects)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(weaponType);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(effects);
        if (record.Effect.Parameters is not { } parameters || record.Intersection.HitObject is not { } struck)
        {
            return;
        }

        bool boned = world.IsBoned(struck);
        if (parameters.IsParticle && !boned)
        {
            int fragments = (int)(random.NextRaw() & 1) + 1;
            float a = Angle(random), b = Angle(random), c = Angle(random);
            effects.Fragments(new Igi2ImpactFragments(record.Intersection.Position, fragments, parameters.Model, record.Direction, (a, b, c)));
        }

        if (world.IsWater(struck))
        {
            effects.WaterSplash(new Igi2WaterSplash(record.Intersection, record.Penetration, parameters));
            return;
        }

        string hole = record.Continues && parameters.PenetratingBulletHole.Length != 0
            ? parameters.PenetratingBulletHole
            : parameters.NonPenetratingBulletHole;
        if (hole.Length != 0)
        {
            effects.BulletHole(new Igi2BulletHoleMark(struck, record.Intersection, hole, parameters.BulletHoleSize));
        }

        if (parameters.IsSmoke && !boned && (random.NextRaw() & 1) != 0)
        {
            effects.Smoke(Smoke(record, (new Fpu(weaponType.SmokeParticleSize) * 4096.0f).F, random));
        }
    }

    /// <summary>One of the explosion's three angles: a sixteen-bit draw over a tenth of a turn, kept as a float.</summary>
    private static float Angle(IgiRandom random) => ((new Fpu(random.NextDouble()) * FragmentSpan)).F;

    /// <summary>
    /// The two puffs: from twelve centimetres off the face along its unit normal, the second a quarter of the size higher;
    /// rising at a speed, strength and spin each drawn from the run stream, the second rising a quarter faster.
    /// </summary>
    private static Igi2ImpactSmoke Smoke(Igi2GunShotHitRecord record, float size, IgiRandom random)
    {
        Vector3f n = record.Intersection.Normal;
        Fpu nx = n.X, ny = n.Y, nz = n.Z;
        if (nx.Value != 0.0 || ny.Value != 0.0 || nz.Value != 0.0)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((nx * nx) + (ny * ny)) + (nz * nz));
            nx = inverse * nx;
            ny = inverse * ny;
            nz = inverse * nz;
        }

        Vector3d at = record.Intersection.Position;
        Fpu z = nz * SmokeOffset;
        Vector3d first = new(((nx * SmokeOffset) + at.X).Value, ((ny * SmokeOffset) + at.Y).Value, (z + at.Z).Value);
        Vector3d second = new(first.X, first.Y, ((new Fpu(size) * 0.25) + first.Z).Value);

        float rise = ((((new Fpu(random.NextDouble()) * 0.75) + 0.15) * 4096000.0f) * 9.2592591e-6f).F;
        float intensity = ((new Fpu(random.NextDouble()) * 0.25) + 0.75).F;
        float firstSpin = Spin(random);
        float secondRise = (new Fpu(rise) * 1.25f).F;
        float secondSpin = Spin(random);
        return new Igi2ImpactSmoke(size, first, second, rise, secondRise, intensity, firstSpin, secondSpin);
    }

    /// <summary>A puff's spin: a draw centred on zero, doubled, over three degrees, about three quarters of a degree.</summary>
    private static float Spin(IgiRandom random)
    {
        Fpu centred = new Fpu(random.NextDouble()) - 0.5f;
        return (((centred + centred) * 0.052359879f) + 0.012799081f).F;
    }
}
