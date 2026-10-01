using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>Binds one <see cref="TerrainHeightGrid"/> to world space, so a chunk can sample it.</summary>
/// <remarks>
/// <para>
/// The original keeps this as a 48-byte run-time record at <c>0xBA2030</c>, indexed by the heightmap id a
/// chunk descriptor carries. The record is <em>not</em> in any terrain file: it is published by a level
/// script's tasks, and the id a chunk gets comes from a global the same system sets rather than from the
/// octree.
/// </para>
/// <para>
/// <see cref="TerrainHeightBindings"/> reconstructs those records from the level's <c>HeightMap</c>
/// tasks and resolves which one a chunk or a collision leaf falls under.
/// </para>
/// </remarks>
/// <param name="Grid">The grid to sample.</param>
/// <param name="OriginX">The world X the grid's first column sits at, in engine units.</param>
/// <param name="OriginY">The world Y the grid's first row sits at, in engine units.</param>
/// <param name="Scale">Grid cells per engine unit.</param>
public readonly record struct TerrainHeightBinding(
    TerrainHeightGrid Grid,
    double OriginX,
    double OriginY,
    double Scale)
{
    /// <summary>Gets a value indicating whether this binding can be sampled.</summary>
    public bool IsBound => !Grid.Corners.IsEmpty && Grid.Size > 0;

    /// <summary>Samples the height at a world position.</summary>
    /// <param name="worldX">The world X in engine units.</param>
    /// <param name="worldY">The world Y in engine units.</param>
    /// <returns>The displacement in engine units, zero when nothing is bound.</returns>
    public double Sample(double worldX, double worldY) =>
        IsBound ? Grid.Sample((worldX - OriginX) * Scale, (worldY - OriginY) * Scale) : 0.0;
}
