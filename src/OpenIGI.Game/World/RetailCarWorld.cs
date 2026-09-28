using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The world a <see cref="RetailCarBody"/> steps in during play.</summary>
/// <remarks>
/// The wheel and collision rays go to <see cref="RetailTerrainRay"/>, which answers the terrain exactly as
/// the original does. Two parts of the original query are not ported yet, and a car meets them as nothing:
/// placed models (the op-9/10 ray handler <c>0x4C1840</c>, so a car does not yet drive onto a bridge or a
/// ramp), and the upward burial probe (<c>0x4CA010</c>), which only a car with non-wheel collision points
/// (the buggy) ever casts.
/// </remarks>
/// <param name="terrain">The level's exact terrain query.</param>
public sealed class RetailCarWorld(RetailTerrainRay terrain) : IRetailCarWorld
{
    private readonly RetailTerrainRay _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));

    /// <inheritdoc/>
    public void CastRays(RetailCarBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
    {
        for (int k = 0; k < results.Length; k++)
        {
            results[k] = new RetailRayResult { Point = ends[k] };
        }

        _terrain.Cast(starts, ends, results);
    }

    /// <inheritdoc/>
    public void ProbeUp(RetailCarBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results)
    {
        for (int k = 0; k < results.Length; k++)
        {
            results[k] = default;
        }
    }

    /// <inheritdoc/>
    public bool VetoContact(RetailCarBody car, in RetailRayResult result) => false;
}
