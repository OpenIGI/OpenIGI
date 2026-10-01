using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>One clipmap level of the level's distance field, as a pass that marches it needs to see it.</summary>
/// <param name="Origin">
/// The low corner of the level's box, RELATIVE TO THE CAMERA. Camera-relative because that is the space
/// every reflection works in: a level tens of kilometres from the world origin has no precision left in
/// a float by the time it reaches a shader, and the march is a loop of small steps.
/// </param>
/// <param name="VoxelSize">How wide one voxel of this level is, in metres. The march's step floor.</param>
/// <param name="Range">
/// What a stored value of one means, in metres. The fields hold an unsigned distance normalised into
/// [0, 1] per level, so reading one without its range gives a number that is not a distance at all.
/// </param>
public readonly record struct DistanceFieldLevel(Vector3f Origin, float VoxelSize, float Range);

/// <summary>
/// The level's global distance field, handed across the renderer seam so a pass can march it without
/// knowing what built it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> A reflection needs to know what a ray hits. Where the adapter can trace, an
/// acceleration structure answers that; where it cannot, the only description of the world a shader can
/// walk is the distance field — and until this existed it lived entirely on the game's side of the seam,
/// so the backend's own reflection passes could not reach it. Glass on a machine without ray tracing
/// therefore reflected the sky and nothing else.
/// </para>
/// <para>
/// The reference does exactly this: its software tracing marches a global distance field and resolves
/// what it hits against the same surface cache the hardware path uses. Nothing here is a lesser
/// approximation of the traced answer — it is a different way of finding the hit, feeding the identical
/// resolve.
/// </para>
/// <para>
/// <b>Four levels, by value.</b> The shader declares four and the settings clamp to four, so this
/// carries them as fields rather than an array: a record struct with a list would allocate on a handover
/// that happens every frame, and the fixed shape is the shader's own.
/// </para>
/// </remarks>
/// <param name="Clipmaps">The volume texture holding every level, stacked as the build wrote them.</param>
/// <param name="Levels">How many levels are real; zero means there is no field to march.</param>
/// <param name="Resolution">How many voxels square one level is.</param>
/// <param name="Level0">The finest level, and the one whose voxel size sets the march's step floor.</param>
/// <param name="Level1">The second level.</param>
/// <param name="Level2">The third level.</param>
/// <param name="Level3">The coarsest level, whose box is as far as the march can see.</param>
public readonly record struct DistanceFieldBinding(
    TextureHandle Clipmaps,
    int Levels,
    int Resolution,
    DistanceFieldLevel Level0,
    DistanceFieldLevel Level1,
    DistanceFieldLevel Level2,
    DistanceFieldLevel Level3)
{
    /// <summary>How many levels the shader, and therefore this, can carry.</summary>
    public const int MaxLevels = 4;

    /// <summary>A handover of nothing, under which no pass can march.</summary>
    public static DistanceFieldBinding None { get; }

    /// <summary>Gets whether there is a field here worth marching.</summary>
    /// <remarks>
    /// All or nothing, as <see cref="SurfaceCacheBinding.IsPresent"/> is and for the same reason: a
    /// march with a volume and no levels reads an unbound sampler, which is not a coarser answer but an
    /// arbitrary one.
    /// </remarks>
    public bool IsPresent => Clipmaps.IsValid && Levels > 0 && Resolution > 0;

    /// <summary>Gets one level by index.</summary>
    /// <param name="level">The level, from zero to three.</param>
    /// <returns>That level's box and scale.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index names no level this can carry.</exception>
    public DistanceFieldLevel this[int level] => level switch
    {
        0 => Level0,
        1 => Level1,
        2 => Level2,
        3 => Level3,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}
