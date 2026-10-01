namespace OpenIGI.Shaders;

/// <summary>
/// Chooses which compiled form of <see cref="ShaderModuleId.UpscaleRejectShading"/> an adapter can run.
/// </summary>
/// <remarks>
/// <para>
/// The pass tiles a five-stage chain of 3x3 filters through group-shared memory, and the tiles are the
/// group plus an apron, so their cost grows with the group and not with the frame. At the preferred
/// sixteen-wide group that is 46.5 KiB, which fits the 48 KiB a desktop adapter reports and does not fit
/// the 32 KiB Metal allows. Exceeding the limit is not a slow path: pipeline creation fails outright, so
/// the choice has to be made before the module is handed to the driver.
/// </para>
/// <para>
/// The two forms differ only in the define the build compiled them with. The measure is identical; the
/// narrower group simply carries a worse apron ratio, loading a 20x20 tile to shade 64 pixels where the
/// wider one loads 28x28 to shade 256.
/// </para>
/// </remarks>
public static class UpscaleRejectShadingVariants
{
    /// <summary>The group size the pass is written for where shared memory allows it.</summary>
    public const int PreferredGroupSize = 16;

    /// <summary>The group size to fall back to where it does not.</summary>
    public const int CompactGroupSize = 8;

    /// <summary>Gets the variant built at <see cref="PreferredGroupSize"/>.</summary>
    public static ShaderVariant Preferred => new("group16");

    /// <summary>Gets the variant built at <see cref="CompactGroupSize"/>.</summary>
    public static ShaderVariant Compact => new("group8");

    /// <summary>Gets how much shared memory the module claims at a given group size.</summary>
    /// <param name="groupSize">The group's edge, in pixels.</param>
    /// <returns>The total size of the six tiles, in bytes.</returns>
    /// <remarks>
    /// The six tiles the source declares, each a float per channel: two at radius six, one at five and
    /// three at four. <c>UpscaleRejectShadingVariantTests</c> reads the radii back out of the Slang source,
    /// so the two cannot drift apart without a test saying so.
    /// </remarks>
    public static int SharedBytes(int groupSize)
    {
        static int Tile(int groupSize, int radius)
        {
            int edge = groupSize + (2 * radius);
            return edge * edge * 3 * sizeof(float);
        }

        return (2 * Tile(groupSize, 6)) + Tile(groupSize, 5) + (3 * Tile(groupSize, 4));
    }

    /// <summary>Chooses the widest variant an adapter's shared memory can hold.</summary>
    /// <param name="maxSharedMemoryBytes">What the adapter reports it allows one group.</param>
    /// <returns>The variant to load, and the group edge it dispatches in.</returns>
    public static (ShaderVariant Variant, int GroupSize) Select(uint maxSharedMemoryBytes) =>
        maxSharedMemoryBytes >= (uint)SharedBytes(PreferredGroupSize)
            ? (Preferred, PreferredGroupSize)
            : (Compact, CompactGroupSize);
}
