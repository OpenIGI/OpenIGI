namespace OpenIGI.Game.World;

/// <summary>
/// How the level's distance field is resampled into clipmaps around the camera.
/// </summary>
/// <remarks>
/// <para>
/// A trace cannot afford to consult every object in the level at every step. A clipmap is the standard
/// answer: a small grid that follows the camera, holding the distance to whatever is nearby, so a step
/// is one texture fetch regardless of how much geometry the level contains.
/// </para>
/// <para>
/// <b>Several of them, each twice the size of the last.</b> One grid cannot be both fine enough to
/// resolve a doorway and large enough to reach the horizon. Nesting them gives fine detail where the
/// ray starts — which is where it takes most of its steps — and coarse detail far away, where a ray is
/// crossing open ground and a metre of error costs nothing.
/// </para>
/// </remarks>
/// <param name="Levels">
/// How many nested grids. Each covers twice the extent of the one inside it at the same voxel count, so
/// each costs the same to build and store.
/// </param>
/// <param name="Resolution">
/// Voxels along each axis of every level. The cost of a rebuild is this cubed, so it is the dial that
/// matters.
/// </param>
/// <param name="NearExtent">
/// How many metres across the innermost level is. Its voxels are this divided by
/// <paramref name="Resolution"/>.
/// </param>
/// <param name="CellsPerAxis">
/// How many cells each level is divided into for culling. Each cell holds the list of objects that
/// reach into it, so a voxel consults a handful of candidates rather than the whole level.
/// </param>
public readonly record struct GlobalDistanceFieldSettings(
    int Levels = 4,
    int Resolution = 64,
    float NearExtent = 4096f,
    int CellsPerAxis = 8)
{
    /// <summary>Gets the settings the shipped presets use.</summary>
    /// <remarks>
    /// Spelled out rather than written <c>new()</c>: for a record struct that is the ZERO instance, not
    /// the primary constructor's defaults, and a resolution of nought builds nothing while reporting
    /// success. That trap has already cost this project one silent stage.
    /// </remarks>
    public static GlobalDistanceFieldSettings Default { get; } = new(Levels: 4);

    /// <summary>Gets how many voxels one level holds.</summary>
    public int VoxelsPerLevel => Resolution * Resolution * Resolution;

    /// <summary>Gets how many cells one level is divided into.</summary>
    public int CellsPerLevel => CellsPerAxis * CellsPerAxis * CellsPerAxis;

    /// <summary>Gets how many voxels a cell spans on each axis.</summary>
    public int VoxelsPerCell => Math.Max(Resolution / Math.Max(CellsPerAxis, 1), 1);

    /// <summary>Gets how many metres across a level is.</summary>
    /// <param name="level">The level, zero being the innermost.</param>
    /// <returns>The extent in metres.</returns>
    public float ExtentOf(int level) => NearExtent * (1 << Math.Clamp(level, 0, Levels - 1));

    /// <summary>Gets how many metres one voxel of a level spans.</summary>
    /// <param name="level">The level, zero being the innermost.</param>
    /// <returns>The voxel size in metres.</returns>
    public float VoxelSizeOf(int level) => ExtentOf(level) / Resolution;

    /// <summary>Gets a value indicating whether these settings describe anything buildable.</summary>
    public bool Enabled => Levels > 0 && Resolution > 1 && NearExtent > 0f && CellsPerAxis > 0;
}
