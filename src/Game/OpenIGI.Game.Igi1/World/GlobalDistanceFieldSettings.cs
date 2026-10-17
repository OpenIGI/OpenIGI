using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

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
/// <para>
/// The world these cover IS in metres — <c>WorldScene</c> divides every raw coordinate by 4096 on
/// load, and the player's eye sits 1.8 above the ground to prove it. This was 4096 for a while, on a
/// theory that the units were raw and a metre was a hundred of them; that made the finest voxel 64
/// metres, so the sun trace started its rays 96 metres off every surface and called a hit anything
/// within 32 — which in a built-up level is everywhere, and every card sat in shadow.
/// </para>
/// </param>
/// <param name="CellsPerAxis">
/// How many cells each level is divided into for culling. Each cell holds the list of objects that
/// reach into it, so a voxel consults a handful of candidates rather than the whole level.
/// </param>
public readonly record struct GlobalDistanceFieldSettings(
    int Levels = 4,
    int Resolution = 64,
    float NearExtent = 64f,
    int CellsPerAxis = 8)
{
    /// <summary>Gets the settings the shipped presets use.</summary>
    /// <remarks>
    /// <para>
    /// Spelled out rather than written <c>new()</c>: for a record struct that is the ZERO instance, not
    /// the primary constructor's defaults, and a resolution of nought builds nothing while reporting
    /// success. That trap has already cost this project one silent stage.
    /// </para>
    /// <para>
    /// A 16-metre near extent (not the 64 the record defaults to) is what seals interiors. Changing it
    /// costs nothing -- the voxel count is the same either way, this only chooses how much world it
    /// covers -- so the near level is pulled in to a 0.25-metre voxel, fine enough to register the
    /// building's walls. At 64 the finest voxel was a whole metre, thinner than any interior wall, so
    /// the radiance cache's probe rays sailed through the walls of a sealed room and came back carrying
    /// the daylight sky: a windowless office lit as if it had a skylight. The trade is far reach, which
    /// drops from 512 metres to 128; validated on mission 1 as ample -- the sealed base-floor office now
    /// darkens to its own ceiling tubes while the windowed office still fills through the glass.
    /// </para>
    /// </remarks>
    public static GlobalDistanceFieldSettings Default { get; } = new(Levels: 4, NearExtent: 16f);

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
