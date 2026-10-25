using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>A mesh at a place, quantised so that the same placement is recognised as the same one.</summary>
/// <param name="MeshId">The mesh placed.</param>
/// <param name="Rotation">A hash of its rotation, each term rounded to a twentieth.</param>
/// <param name="X">Its position across, in two-centimetre cells.</param>
/// <param name="Y">Its position up, in two-centimetre cells.</param>
/// <param name="Z">Its position along, in two-centimetre cells.</param>
/// <remarks>
/// <para>
/// NOT bit-identical frame to frame, which is what this once assumed: the transform is recovered from a
/// camera-relative model-view through the frame's own rotation, in single precision, so its translation
/// wanders by about half a millimetre as the camera turns. See <see cref="WorldScene.KnownPlacement"/>, which
/// matches a key a cell either side of an existing one for exactly that reason.
/// </para>
/// <para>
/// The rotation is rounded to a twentieth: its own noise is around a ten-millionth, and the values
/// placements actually take -- nought, a half, one, and the arbitrary angles between -- sit nowhere near
/// a boundary that fine noise could cross. The position is rounded to two centimetres, coarse enough
/// that the neighbour search covers any wander and fine enough that two copies of one mesh never share.
/// </para>
/// </remarks>
internal readonly record struct WorldScenePlacement(uint MeshId, long Rotation, long X, long Y, long Z)
{
    private const float RotationSteps = 20f;
    private const float CellsPerMetre = 50f;

    /// <summary>The placement of a mesh drawn at a transform.</summary>
    /// <param name="meshId">The mesh drawn.</param>
    /// <param name="m">Where it is drawn, in absolute world space.</param>
    /// <returns>Its key, quantised.</returns>
    public static WorldScenePlacement From(uint meshId, in Matrix4f m)
    {
        static long Step(float value) => (long)MathF.Round(value * RotationSteps);

        long rotation = (Step(m.M11) * 73856093) ^ (Step(m.M12) * 19349663) ^ (Step(m.M13) * 83492791)
            ^ (Step(m.M21) * 32452843) ^ (Step(m.M22) * 49979687) ^ (Step(m.M23) * 86028121)
            ^ (Step(m.M31) * 15485863) ^ (Step(m.M32) * 32452867) ^ (Step(m.M33) * 67867967);

        return new WorldScenePlacement(
            meshId,
            rotation,
            (long)MathF.Floor(m.M14 * CellsPerMetre),
            (long)MathF.Floor(m.M24 * CellsPerMetre),
            (long)MathF.Floor(m.M34 * CellsPerMetre));
    }
}
