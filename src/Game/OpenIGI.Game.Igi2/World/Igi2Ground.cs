using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The ground an I.G.I. 2 level puts under a body: the height field its tiles describe, sampled the way
/// the original samples it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole query is <c>Terrain_VertexFunction</c> (<c>igi2 0x5706F0</c>).</b> It picks the tile,
/// interpolates the four samples around the point over one of the cell's two triangles, and returns the
/// height <em>relative to the level's terrain</em> together with that triangle's normal.
/// <c>Terrain_GetNextZHandler</c> (<c>0x572E80</c>) — the handler every ground query in the game reaches
/// through — then adds the <c>Terrain</c> task's own Z and keeps the surface only when it falls between
/// the two ends of the probe.
/// </para>
/// <para>
/// <b>The normal is the triangle's, not a smoothed one.</b> The original derives it from the same two
/// differences the height came from, so the surface a body slides on is exactly the surface it stands
/// on. (<c>TerrainMap_GetNormal</c> takes a central difference over neighbouring samples instead, but
/// that one belongs to the lightmap bake and nothing else calls it.)
/// </para>
/// <para>
/// <b>A <see cref="DiscardTask"/> removes the ground here too.</b> <c>Terrain_GetNextZHandler</c> tests
/// the point against <c>DiscardTask_IsDiscardVertex</c> (<c>igi2 0x5D1850</c>) before it accepts a
/// surface, so a body over one of the level's discard boxes gets no terrain at all and falls to
/// whatever collision mesh was built in the hole. The renderer applies the same boxes, which is what
/// keeps the surface a body stands on the surface it can see.
/// </para>
/// <para>
/// <b>Placed objects are not in here.</b> The body's own query already casts at the world's collision
/// meshes and keeps whichever surface is higher, exactly as it does for the first game, so a crate or a
/// building floor still wins over the ground beneath it.
/// </para>
/// </remarks>
public sealed class Igi2Ground : IGroundHeightField
{
    private readonly IReadOnlyList<Igi2TerrainTile> _tiles;
    private readonly Igi2DiscardBox[] _discards;
    private readonly Vector3d _origin;

    /// <summary>Initializes a new instance of the <see cref="Igi2Ground"/> class.</summary>
    /// <param name="surface">The level's loaded tiles.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public Igi2Ground(Igi2TerrainSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _tiles = surface.Tiles;
        _discards = surface.DiscardBoxes;
        _origin = surface.Origin;
    }

    /// <summary>Gets a value indicating whether this level has any ground to stand on.</summary>
    public bool IsEmpty => _tiles.Count == 0;

    /// <inheritdoc/>
    public bool TryGetGround(double x, double y, double z, double aboveUnits, out TerrainGround ground)
    {
        ground = default;
        if (!TrySample(x, y, out TerrainGround surface))
        {
            return false;
        }

        // 0x572E93: the surface counts only when it lies between the two ends of the probe. A body a
        // long way above the ground is not standing on it, and one already under it is not either.
        if (surface.Height > z + aboveUnits || surface.Height < z - TerrainHeightQuery.SearchDepthUnits)
        {
            return false;
        }

        ground = surface;
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A height field makes the swept test exact rather than approximate: the body is above the surface
    /// at one end and below it at the other, so the crossing is where the gap changes sign. Solved by
    /// interpolation along the segment, which is the same answer a triangle sweep would give on ground
    /// this smooth and needs no traversal at all.
    /// </remarks>
    public bool TryTraceDescent(Vector3d from, Vector3d target, out TerrainGround ground)
    {
        ground = default;
        if (target.Z >= from.Z)
        {
            return false;
        }

        if (!TrySample(from.X, from.Y, out TerrainGround above) || from.Z < above.Height)
        {
            return false;
        }

        if (!TrySample(target.X, target.Y, out TerrainGround below) || target.Z > below.Height)
        {
            return false;
        }

        // Where the gap closes. Both gaps are measured the same way round, so the fraction is between
        // zero and one whenever the signs differ, and the normal is the one at the far end -- the
        // surface the body has arrived on rather than the one it left.
        double gapAtStart = from.Z - above.Height;
        double gapAtEnd = target.Z - below.Height;
        double span = gapAtStart - gapAtEnd;
        double fraction = span > 0.0 ? gapAtStart / span : 0.0;

        double height = from.Z + ((target.Z - from.Z) * Math.Clamp(fraction, 0.0, 1.0));
        ground = new TerrainGround(height, below.NormalX, below.NormalY, below.NormalZ);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 ground over {_tiles.Count} tile(s), {_discards.Length} discard(s)";

    // Terrain_GetNextZHandler, 0x572E80: the point relative to the terrain as floats, the ground there
    // (Igi2TerrainVertex), the discard boxes, and the height back in the world at the client's precision.
    private bool TrySample(double x, double y, out TerrainGround ground)
    {
        ground = default;
        float relativeX = ((Fpu)x - _origin.X).F;
        float relativeY = ((Fpu)y - _origin.Y).F;
        if (!Igi2TerrainVertex.TrySample(_tiles, relativeX, relativeY, out Igi2TerrainSample sample)
            || Igi2DiscardBox.AnyContains(_discards, relativeX, relativeY))
        {
            return false;
        }

        double height = ((Fpu)sample.RelativeZ + _origin.Z).Value;
        ground = new TerrainGround(height, sample.Normal.X, sample.Normal.Y, sample.Normal.Z);
        return true;
    }
}
