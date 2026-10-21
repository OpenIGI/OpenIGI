using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The ground a level puts under a body: how high it is, and which way it faces.</summary>
/// <remarks>
/// <para>
/// <b>Two games, one question, two completely different answers.</b> Project I.G.I. stores its ground as
/// an octree of geometry blocks and answers by descending it to a leaf and intersecting a triangle;
/// I.G.I. 2 stores a grid of height samples and answers by interpolating four of them. Nothing about
/// either shape survives into the other, and the body standing on the result does not care: it asks how
/// high the ground is at a point and which way the surface faces, and both games can say.
/// </para>
/// <para>
/// <b>This is deliberately not "the terrain".</b> A terrain is also a thing you ray-trace through, draw,
/// bake lightmaps from and block sight with, and those stay on the concrete type that can do them —
/// <see cref="TerrainRayTrace"/> and <c>TerrainSightBlock</c> still take a
/// <see cref="TerrainData"/>, because there is nothing to share yet. What is behind this seam is only
/// what the player's feet need, which is the part I.G.I. 2 can already answer.
/// </para>
/// <para>
/// The window is part of the contract rather than an implementation detail. The original's own ground
/// query is a <em>segment</em> test — <c>Terrain_GetNextZHandler</c> (<c>igi2 0x572E80</c>) accepts a
/// surface only when its height lies between the two ends of the probe, and Project I.G.I.'s
/// (<c>0x4CB050</c>) reaches <see cref="TerrainHeightQuery.SearchDepthUnits"/> below its origin — so a
/// query is asking "is there ground in this stretch", never "how high is the ground somewhere below".
/// </para>
/// </remarks>
public interface IGroundHeightField
{
    /// <summary>Finds the ground under — or just above — a point.</summary>
    /// <param name="x">The point's X, in world units.</param>
    /// <param name="y">The point's Y, in world units.</param>
    /// <param name="z">The point's Z: the middle of the search window.</param>
    /// <param name="aboveUnits">
    /// How far above <paramref name="z"/> a surface is still accepted. Zero keeps the near-ground
    /// semantics a standing body wants: only a surface at or below the point answers.
    /// </param>
    /// <param name="ground">Receives the surface when one is found.</param>
    /// <returns>
    /// <see langword="false"/> when no surface lies between <paramref name="z"/> + <paramref name="aboveUnits"/>
    /// and <paramref name="z"/> − <see cref="TerrainHeightQuery.SearchDepthUnits"/>.
    /// </returns>
    bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground);

    /// <summary>Finds the ground under a point, accepting nothing above it.</summary>
    /// <param name="x">The point's X, in world units.</param>
    /// <param name="y">The point's Y, in world units.</param>
    /// <param name="z">The height to search down from.</param>
    /// <param name="ground">Receives the surface when one is found.</param>
    /// <returns><see langword="false"/> when nothing is under the point within the search depth.</returns>
    bool TryGetGround(double x, double y, double z, out TerrainGround ground) =>
        TryGetGround(x, y, z, aboveUnits: 0.0, out ground);

    /// <summary>Finds the surface a body crossed on its way down between two points.</summary>
    /// <param name="from">Where the body was.</param>
    /// <param name="target">Where it would be.</param>
    /// <param name="ground">Receives the surface it crossed.</param>
    /// <returns><see langword="false"/> when the move crossed no upward-facing ground.</returns>
    /// <remarks>
    /// The continuous half of the query, and the reason a fall does not tunnel. A damaging drop covers
    /// more in one tick than the ordinary probe reaches below the feet, so a query at the destination
    /// alone starts <em>under</em> the surface and never sees it again. Both originals cast the move
    /// itself before the endpoint query for exactly this; only Z is taken from the result, because the
    /// body is placed at its own X and Y.
    /// </remarks>
    bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground);
}
