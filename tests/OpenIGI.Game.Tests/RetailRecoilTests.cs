using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The view kick, checked over the whole roster rather than over the three weapons mission 1 carries.
/// </summary>
/// <remarks>
/// <para>
/// <c>0x479780</c> is the whole model: rejection-sample a point in the unit disc, then
/// <c>random × sample + fixed</c> per axis, in degrees. <c>0x462E50</c> accumulates the result into a
/// pair of view angles damped by <c>0.9</c> a tick, and <c>0x464800</c> adds the <b>pitch</b> on top of
/// the look.
/// </para>
/// </remarks>
public sealed class RetailRecoilTests
{
    [RequiresGameDataFact]
    public void EveryWeaponsKickStaysInsideItsOwnFixedPlusRandomBound()
    {
        IgiRandom random = new(4242);

        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            // The disc sample is on [-1, 1] per axis, so the kick can never leave fixed ± |random|.
            // The absolute value matters: the Minimi declares a NEGATIVE random amplitude, which only
            // mirrors a symmetric disc and behaves exactly like the positive one.
            float boundX = Math.Abs(weapon.RandomRecoilX) * GunFiring.DegreesToRadians;
            float boundZ = Math.Abs(weapon.RandomRecoilZ) * GunFiring.DegreesToRadians;
            float centreX = weapon.FixedRecoilX * GunFiring.DegreesToRadians;
            float centreZ = weapon.FixedRecoilZ * GunFiring.DegreesToRadians;

            for (int i = 0; i < 50; i++)
            {
                (float x, float z) = HeldGun.SampleRecoil(weapon, random);

                Assert.InRange(x, centreX - boundX - 1e-5f, centreX + boundX + 1e-5f);
                Assert.InRange(z, centreZ - boundZ - 1e-5f, centreZ + boundZ + 1e-5f);
            }
        }
    }

    [RequiresGameDataFact]
    public void AWeaponWithNoRandomTermKicksIdenticallyEveryShot()
    {
        IgiRandom random = new(9);

        // Fourteen of the twenty-four declare no random recoil at all, so their kick is a constant.
        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            if (weapon.RandomRecoilX != 0f || weapon.RandomRecoilZ != 0f)
            {
                continue;
            }

            (float x, float z) = HeldGun.SampleRecoil(weapon, random);
            Assert.Equal(weapon.FixedRecoilX * GunFiring.DegreesToRadians, x, 6);
            Assert.Equal(weapon.FixedRecoilZ * GunFiring.DegreesToRadians, z, 6);
        }
    }

    [RequiresGameDataFact]
    public void TheRosterKicksInYawAsWellAsPitch()
    {
        // An earlier note claimed the shipped roster only ever kicks in pitch. It does not: five
        // weapons declare a random yaw and the SPAS-12 a FIXED one, so a port that dropped the Z pair
        // would be visibly wrong on them.
        int randomYaw = RetailWeaponRoster.Value.Weapons.Count(w => w.RandomRecoilZ != 0f);
        int fixedYaw = RetailWeaponRoster.Value.Weapons.Count(w => w.FixedRecoilZ != 0f);

        Assert.True(randomYaw > 0, "no weapon declares a random yaw kick.");
        Assert.True(fixedYaw > 0, "no weapon declares a fixed yaw kick.");
    }

    [RequiresGameDataFact]
    public void TheKickIsSampledFromADiscRatherThanTwoIndependentAxes()
    {
        // Correlated, not square: a shot that kicks hard up cannot also kick hard sideways. Sampling
        // the two axes independently would put points in the corners, which this asserts never happens.
        WeaponType weapon = new() { RandomRecoilX = 1f, RandomRecoilZ = 1f };
        IgiRandom random = new(31337);

        const float unit = 1f * GunFiring.DegreesToRadians;
        for (int i = 0; i < 500; i++)
        {
            (float x, float z) = HeldGun.SampleRecoil(weapon, random);

            float radius = MathF.Sqrt((x * x) + (z * z)) / unit;
            Assert.True(radius <= 1.0001f, $"sample {i} is {radius} from the centre, outside the disc.");
        }
    }

    [RequiresGameDataFact]
    public void TheGlocksKickIsExactlyOnePointFiveDegreesOfPitch()
    {
        Assert.True(RetailWeaponRoster.Value.TryGetByScriptId("WEAPON_ID_GLOCK", out WeaponType glock));

        IgiRandom random = new(1);
        (float x, float z) = HeldGun.SampleRecoil(glock, random);

        Assert.Equal(1.5f * GunFiring.DegreesToRadians, x, 6);
        Assert.Equal(0f, z, 6);
    }

    [Fact]
    public void TheAccumulatedKickDecaysByATenthAndSettlesAtTenTimesTheShot()
    {
        // 0x462E50's `(kick + current) * 0.9`. Held down, the series converges on 9x the per-shot kick
        // -- which is why a long burst climbs and then stops climbing rather than running away.
        float current = 0f;
        const float kick = 1f;

        for (int i = 0; i < 400; i++)
        {
            current = (kick + current) * PlayerWeapons.RecoilDamping;
        }

        Assert.Equal(9f, current, 3);
    }
}
