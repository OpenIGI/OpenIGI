using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>Why a trigger pull did not produce a round.</summary>
public enum GunFireBlock
{
    /// <summary>Nothing blocked it; the round goes off.</summary>
    None,

    /// <summary>The trigger is not down.</summary>
    TriggerReleased,

    /// <summary>This pull has already fired its whole burst.</summary>
    BurstComplete,

    /// <summary>The previous round's cooldown has not run out.</summary>
    Cooling,

    /// <summary>The magazine is empty.</summary>
    Empty,
}

/// <summary>
/// The gate a trigger pull passes through, and the cone the round comes out in.
/// </summary>
/// <remarks>
/// Transcribed from <c>Gun_Fire</c> (<c>0x478BA0</c>), which tests the three conditions in this order
/// and clears its "firing" flag at each.
/// </remarks>
public static class GunFiring
{
    /// <summary>
    /// How many rounds of a burst go out at the minimum spread before the maximum takes over.
    /// </summary>
    /// <remarks>
    /// <b>A step, not a ramp.</b> The original writes <c>if (roundsThisBurst &gt; 3)</c>, so rounds one
    /// to four use <see cref="WeaponType.MinimumSpread"/> and everything after uses
    /// <see cref="WeaponType.MaximumSpread"/> — there is no interpolation between them anywhere.
    /// </remarks>
    public const int RoundsAtMinimumSpread = 3;

    /// <summary>Degrees to radians, as the single-precision constant the firing path uses.</summary>
    public const float DegreesToRadians = 0.017453292f;

    /// <summary>A full turn, as the original's literal.</summary>
    public const float TwoPi = 6.2831855f;

    /// <summary>How many random samples the cone's radius is the product of.</summary>
    /// <remarks>
    /// Three, multiplied together. Each is uniform on 0–1, so their product is heavily biased toward
    /// zero — the shot group is dense at the centre and sparse at the rim, rather than uniform over the
    /// disc. That is the whole of the accuracy model.
    /// </remarks>
    public const int ConeRadiusSamples = 3;

    /// <summary>Decides whether a trigger pull produces a round.</summary>
    /// <param name="weapon">The weapon being fired.</param>
    /// <param name="state">The gun's current state.</param>
    /// <param name="triggerHeld">Whether the trigger is down this tick.</param>
    /// <param name="consumesAmmunition">
    /// Whether the gun has an ammunition record. A gun with none — the emplacement guns — never runs
    /// out, because the original only tests the magazine when the record is present.
    /// </param>
    /// <returns>What stopped the round, or <see cref="GunFireBlock.None"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The burst test compares against <see cref="WeaponType.MaximumRoundsPerBurst"/>, which is 1 on a
    /// semi-automatic and the whole magazine on an automatic — so the same comparison gives both
    /// behaviours without a mode flag anywhere.
    /// </remarks>
    public static GunFireBlock Evaluate(
        WeaponType weapon,
        GunState state,
        bool triggerHeld,
        bool consumesAmmunition)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        if (!triggerHeld)
        {
            return GunFireBlock.TriggerReleased;
        }

        if (state.RoundsThisBurst == weapon.MaximumRoundsPerBurst)
        {
            return GunFireBlock.BurstComplete;
        }

        if (state.CooldownTicks != 0)
        {
            return GunFireBlock.Cooling;
        }

        if (consumesAmmunition && state.RoundsInClip == 0)
        {
            return GunFireBlock.Empty;
        }

        return GunFireBlock.None;
    }

    /// <summary>Picks the spread the next round comes out in.</summary>
    /// <param name="weapon">The weapon being fired.</param>
    /// <param name="roundsThisBurst">How many rounds this trigger pull has already fired.</param>
    /// <returns>The half-angle of the cone in radians, always non-negative.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original takes the absolute value <i>after</i> converting, which matters because most of the
    /// roster's minimum spread is negative — the shipped data stores the pair as a signed range about
    /// zero, so without the absolute value the first rounds of every burst would have a negative cone.
    /// </remarks>
    public static float ConeAngle(WeaponType weapon, int roundsThisBurst)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        float degrees = roundsThisBurst > RoundsAtMinimumSpread
            ? weapon.MaximumSpread
            : weapon.MinimumSpread;

        float radians = degrees * DegreesToRadians;
        return radians < 0f ? -radians : radians;
    }

    /// <summary>Turns a direction by a sampled cone offset — <c>0x478BA0</c>'s rotation about the barrel.</summary>
    /// <param name="forward">The barrel's direction.</param>
    /// <param name="right">A unit vector perpendicular to it.</param>
    /// <param name="up">The third axis.</param>
    /// <param name="radius">The offset angle from <see cref="SampleCone"/>.</param>
    /// <param name="bearing">The bearing about the barrel from <see cref="SampleCone"/>.</param>
    /// <returns>The round's direction, normalised.</returns>
    /// <remarks>
    /// The original builds a basis from the direction (<c>0x414A60</c>), makes a rotation from the two angles
    /// (<c>0x4B38E0</c>) and multiplies the two 3x3s; the product's forward column is what this computes.
    /// </remarks>
    public static Vector3f OffsetInCone(Vector3f forward, Vector3f right, Vector3f up, float radius, float bearing)
    {
        float sin = MathF.Sin(radius);
        Vector3f lateral = (right * (MathF.Cos(bearing) * sin)) + (up * (MathF.Sin(bearing) * sin));
        return ((forward * MathF.Cos(radius)) + lateral).Normalized();
    }

    /// <summary>Turns a direction by a sampled cone offset exactly as <c>0x478BA0</c> does.</summary>
    /// <param name="forward">The view's forward, the column the fire message carries.</param>
    /// <param name="radius">The offset angle from <see cref="SampleCone"/>.</param>
    /// <param name="bearing">The bearing from <see cref="SampleCone"/>.</param>
    /// <returns>The round's direction: the forward column of <c>basis(forward) · E(radius, bearing, 0)</c>.</returns>
    /// <remarks>
    /// <c>0x414A60</c> builds the basis with the forward as its middle column, a side axis from <c>(y, -x, 0)</c> and
    /// an up axis from their cross product (from <c>(0, -z, y)</c> first when the forward is within 0.95 of
    /// vertical). <c>0x4B38E0</c> is <see cref="Matrix3f.FromEngineEulerAngles"/>.
    /// </remarks>
    public static Vector3f RetailConeDirection(Vector3f forward, float radius, float bearing)
    {
        float x = forward.X;
        float y = forward.Y;
        float z = forward.Z;
        float rx, ry, rz, ux, uy, uz;
        if (Math.Abs(z) <= 0.94999999)
        {
            rx = y;
            ry = -x;
            rz = 0f;
            if (rx != 0f || ry != 0f)
            {
                double inverse = 1.0 / Math.Sqrt((rx * (double)rx) + (ry * (double)ry));
                rx = (float)(rx * inverse);
                ry = (float)(ry * inverse);
                rz = (float)(0.0 * inverse);
            }

            ux = (z * ry) - (y * rz);
            uy = -((z * rx) - (x * rz));
            uz = (y * rx) - (x * ry);
            if (ux != 0f || uy != 0f || uz != 0f)
            {
                double inverse = 1.0 / Math.Sqrt((ux * (double)ux) + (uy * (double)uy) + (uz * (double)uz));
                ux = (float)(ux * inverse);
                uy = (float)(uy * inverse);
                uz = (float)(uz * inverse);
            }
        }
        else
        {
            ux = 0f;
            uy = -z;
            uz = y;
            if (uy != 0f || uz != 0f)
            {
                double inverse = 1.0 / Math.Sqrt((uy * (double)uy) + (uz * (double)uz));
                ux = (float)(0.0 * inverse);
                uy = (float)(uy * inverse);
                uz = (float)(uz * inverse);
            }

            rx = (y * uz) - (z * uy);
            ry = -((x * uz) - (z * ux));
            rz = (x * uy) - (y * ux);
            if (rx != 0f || ry != 0f || rz != 0f)
            {
                double inverse = 1.0 / Math.Sqrt((rx * (double)rx) + (ry * (double)ry) + (rz * (double)rz));
                rx = (float)(rx * inverse);
                ry = (float)(ry * inverse);
                rz = (float)(rz * inverse);
            }
        }

        Matrix3f basis = new(rx, x, ux, ry, y, uy, rz, z, uz);
        Matrix3f turned = basis * Matrix3f.FromEngineEulerAngles(radius, bearing, 0f);
        return new Vector3f(turned.M12, turned.M22, turned.M32);
    }

    /// <summary>Samples one projectile's offset within the cone.</summary>
    /// <param name="coneAngle">The half-angle from <see cref="ConeAngle"/>.</param>
    /// <param name="uniformSamples">
    /// Four uniform samples on 0–1: the first three become the radius, the fourth the bearing.
    /// </param>
    /// <returns>The radius in radians and the bearing in radians.</returns>
    /// <exception cref="ArgumentException"><paramref name="uniformSamples"/> is not four long.</exception>
    /// <remarks>
    /// The caller draws all four from the same generator in this order, so reproducing a shot means
    /// reproducing that order exactly. A <paramref name="coneAngle"/> of zero skips the whole sampling
    /// in the original and fires straight down the barrel.
    /// </remarks>
    public static (float Radius, float Bearing) SampleCone(
        float coneAngle,
        ReadOnlySpan<float> uniformSamples)
    {
        if (uniformSamples.Length != ConeRadiusSamples + 1)
        {
            throw new ArgumentException(
                $"Expected {ConeRadiusSamples + 1} samples, got {uniformSamples.Length}.",
                nameof(uniformSamples));
        }

        float radius = 1f;
        for (int i = 0; i < ConeRadiusSamples; i++)
        {
            radius *= uniformSamples[i];
        }

        return (radius * coneAngle, uniformSamples[ConeRadiusSamples] * TwoPi);
    }
}
