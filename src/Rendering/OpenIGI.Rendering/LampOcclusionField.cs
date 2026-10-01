using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>The level's global distance field, handed to a renderer that shadows its lamps without tracing.</summary>
/// <param name="Clipmaps">The clipmap volume, every level stacked along z, as the gather reads it.</param>
/// <param name="Levels">How many levels are in use, at most four.</param>
/// <param name="Resolution">Voxels along each side of one level.</param>
/// <param name="Corner0">Level 0's low corner, in absolute world metres.</param>
/// <param name="Corner1">Level 1's low corner.</param>
/// <param name="Corner2">Level 2's low corner.</param>
/// <param name="Corner3">Level 3's low corner.</param>
/// <param name="VoxelSizes">Each level's voxel size in metres, one per level.</param>
/// <param name="Ranges">Each level's encoded distance range, one per level.</param>
/// <remarks>
/// A renderer that can trace rays shadows its lamps against the world's acceleration structure and never
/// reads this. One that cannot -- WebGPU, and so every browser -- marches the same rays through this
/// field instead, as the reference's many-lights path does without hardware; without it such a renderer
/// had no lamp shadow at all. The corners are in doubles because the level stands kilometres from the
/// origin, and the renderer makes them relative to its own camera.
/// </remarks>
public readonly record struct LampOcclusionField(
    TextureHandle Clipmaps,
    int Levels,
    int Resolution,
    Vector3d Corner0,
    Vector3d Corner1,
    Vector3d Corner2,
    Vector3d Corner3,
    IReadOnlyList<float> VoxelSizes,
    IReadOnlyList<float> Ranges)
{
    /// <summary>Gets a level's low corner.</summary>
    /// <param name="level">The level, 0 to 3.</param>
    /// <returns>Its corner in absolute world metres.</returns>
    public Vector3d CornerOf(int level) => level switch
    {
        0 => Corner0,
        1 => Corner1,
        2 => Corner2,
        _ => Corner3,
    };
}
