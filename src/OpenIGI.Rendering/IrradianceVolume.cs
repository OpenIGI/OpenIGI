using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// A grid of probes holding how much light arrives at each point in the level, and from where.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a grid of probes at all.</b> The screen-space bounce in the stage before this can only use
/// surfaces the camera can see, so a wall just off screen neither shades nor colours anything, and
/// anything indoors is lit by whatever happens to be in frame. A probe volume knows about the geometry
/// the camera cannot: each probe was asked, once, what it can see in every direction, so a soldier
/// standing in a doorway is lit by the room he is in rather than by the view.
/// </para>
/// <para>
/// <b>Why the visibility is baked and the light is not.</b> The expensive question is
/// <em>geometric</em> — what can this point see — and in this game the answer never changes: the levels
/// are static, and the sun is a fixed direction per mission rather than a moving body. So the transfer
/// is traced once at load and only the colours are applied per frame, which makes relighting a
/// multiply rather than a trace. That is the classic precomputed-transfer bargain, and it is a
/// particularly good one here because the thing it gives up — light that moves — is a thing this game
/// does not have.
/// </para>
/// <para>
/// <b>What it stores.</b> First-order spherical harmonics: one constant plus three directional terms
/// per colour channel. That is four numbers to describe light arriving from every direction at once,
/// and evaluating it for a surface is a dot product. Second order would resolve sharper gradients, at
/// twice the storage and with ringing artefacts that need windowing; L1 is what an irradiance field
/// wants, because irradiance is already a cosine-blurred version of the light around it and has almost
/// no high-frequency content left to resolve.
/// </para>
/// <para>
/// Distances are in metres and positions are absolute world coordinates, matching everything else the
/// renderer is handed.
/// </para>
/// </remarks>
public sealed class IrradianceVolume
{
    /// <summary>How many floats one probe occupies: four coefficients for each of three channels.</summary>
    public const int CoefficientsPerProbe = 12;

    private readonly float[] _coefficients;
    private readonly float[] _distances;

    /// <summary>Initialises a new instance of the <see cref="IrradianceVolume"/> class.</summary>
    /// <param name="origin">The centre of the probe at index (0, 0, 0), in world metres.</param>
    /// <param name="spacing">The distance between neighbouring probes, in metres.</param>
    /// <param name="countX">How many probes span the volume along X.</param>
    /// <param name="countY">How many along Y.</param>
    /// <param name="countZ">How many along Z.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count is below one, or the spacing is not positive.</exception>
    public IrradianceVolume(Vector3f origin, float spacing, int countX, int countY, int countZ)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(countX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(countY, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(countZ, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(spacing, 0f);

        Origin = origin;
        Spacing = spacing;
        CountX = countX;
        CountY = countY;
        CountZ = countZ;

        _coefficients = new float[countX * countY * countZ * CoefficientsPerProbe];
        _distances = new float[countX * countY * countZ * 2];
    }

    /// <summary>Gets the centre of probe (0, 0, 0), in world metres.</summary>
    public Vector3f Origin { get; }

    /// <summary>Gets the distance between neighbouring probes, in metres.</summary>
    public float Spacing { get; }

    /// <summary>Gets how many probes span the volume along X.</summary>
    public int CountX { get; }

    /// <summary>Gets how many probes span the volume along Y.</summary>
    public int CountY { get; }

    /// <summary>Gets how many probes span the volume along Z.</summary>
    public int CountZ { get; }

    /// <summary>Gets how many probes the volume holds.</summary>
    public int ProbeCount => CountX * CountY * CountZ;

    /// <summary>Gets the spherical-harmonic coefficients, twelve per probe, X varying fastest.</summary>
    public ReadOnlySpan<float> Coefficients => _coefficients;

    /// <summary>Gets the mean and mean-squared distance to geometry, two per probe.</summary>
    /// <remarks>
    /// Two moments rather than one because a mean alone cannot tell "everything is two metres away"
    /// from "half of it is touching and half is four metres off", and those two want opposite answers
    /// when deciding whether a probe is on the same side of a wall as the surface asking it. The
    /// variance between them is what <see cref="ChebyshevWeight"/> uses.
    /// </remarks>
    public ReadOnlySpan<float> Distances => _distances;

    /// <summary>Gets the index of a probe in the flat arrays.</summary>
    /// <param name="x">The probe's X index.</param>
    /// <param name="y">Its Y index.</param>
    /// <param name="z">Its Z index.</param>
    /// <returns>The probe's ordinal.</returns>
    public int IndexOf(int x, int y, int z) => x + (CountX * (y + (CountY * z)));

    /// <summary>Gets a probe's centre in world metres.</summary>
    /// <param name="x">The probe's X index.</param>
    /// <param name="y">Its Y index.</param>
    /// <param name="z">Its Z index.</param>
    /// <returns>The position.</returns>
    public Vector3f PositionOf(int x, int y, int z) => new(
        Origin.X + (x * Spacing),
        Origin.Y + (y * Spacing),
        Origin.Z + (z * Spacing));

    /// <summary>Records what a probe receives.</summary>
    /// <param name="probe">The probe's ordinal, from <see cref="IndexOf"/>.</param>
    /// <param name="coefficients">Twelve coefficients: four per channel, red first.</param>
    /// <param name="meanDistance">The mean distance to geometry, in metres.</param>
    /// <param name="meanSquaredDistance">The mean of the squared distances.</param>
    /// <exception cref="ArgumentException"><paramref name="coefficients"/> is not twelve long.</exception>
    public void SetProbe(
        int probe,
        ReadOnlySpan<float> coefficients,
        float meanDistance,
        float meanSquaredDistance)
    {
        if (coefficients.Length != CoefficientsPerProbe)
        {
            throw new ArgumentException(
                $"A probe takes {CoefficientsPerProbe} coefficients, not {coefficients.Length}.",
                nameof(coefficients));
        }

        coefficients.CopyTo(_coefficients.AsSpan(probe * CoefficientsPerProbe, CoefficientsPerProbe));
        _distances[probe * 2] = meanDistance;
        _distances[(probe * 2) + 1] = meanSquaredDistance;
    }

    /// <summary>Evaluates first-order spherical harmonics along a direction.</summary>
    /// <param name="coefficients">One channel's four coefficients.</param>
    /// <param name="direction">The surface normal, which should be unit length.</param>
    /// <returns>The irradiance arriving along that normal, never below zero.</returns>
    /// <remarks>
    /// The weights are the convolution of the L1 basis with a cosine lobe, which is what turns "light
    /// arriving from every direction" into "light a flat surface facing this way receives". Clamped at
    /// zero because an L1 fit to a very directional environment can undershoot, and negative irradiance
    /// is not a dim surface, it is a hole.
    /// </remarks>
    public static float Evaluate(ReadOnlySpan<float> coefficients, Vector3f direction) =>
        MathF.Max(
            coefficients[0]
                + (coefficients[1] * direction.X)
                + (coefficients[2] * direction.Y)
                + (coefficients[3] * direction.Z),
            0f);

    /// <summary>
    /// Weighs how much a probe should be trusted by a surface, from its distance moments.
    /// </summary>
    /// <param name="distanceToProbe">How far the surface is from the probe, in metres.</param>
    /// <param name="meanDistance">The probe's mean distance to geometry.</param>
    /// <param name="meanSquaredDistance">The probe's mean squared distance to geometry.</param>
    /// <returns>A weight from 0 to 1.</returns>
    /// <remarks>
    /// <para>
    /// This is what stops light leaking through walls, which is the failure that makes a naive probe
    /// volume unusable: a probe sitting inside a wall, or outdoors on the far side of one, is a
    /// perfectly good neighbour by distance and a completely wrong one by visibility, and trilinear
    /// interpolation will happily blend it in.
    /// </para>
    /// <para>
    /// Chebyshev's inequality bounds the probability that a value exceeds the mean by a given amount,
    /// given only the mean and the variance. Used here it answers "given that this probe sees geometry
    /// at an average of <paramref name="meanDistance"/> with this much spread, how likely is a point
    /// <paramref name="distanceToProbe"/> away to be on the probe's side of it" — without storing
    /// per-direction depth, which is what the storage saving buys.
    /// </para>
    /// </remarks>
    public static float ChebyshevWeight(float distanceToProbe, float meanDistance, float meanSquaredDistance)
    {
        // Nearer than anything the probe can see: unambiguously on its side, so no doubt to resolve.
        if (distanceToProbe <= meanDistance)
        {
            return 1f;
        }

        float variance = MathF.Max(meanSquaredDistance - (meanDistance * meanDistance), 1e-4f);
        float difference = distanceToProbe - meanDistance;
        float weight = variance / (variance + (difference * difference));

        // Cubed, which sharpens the falloff. The bound is loose by construction -- it holds for every
        // distribution, so it is generous about the ones it is actually given -- and taken at face
        // value it lets through exactly the half-lit wall the test exists to prevent.
        return weight * weight * weight;
    }
}
