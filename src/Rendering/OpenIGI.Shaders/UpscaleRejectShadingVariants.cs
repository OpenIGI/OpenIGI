namespace OpenIGI.Shaders;

/// <summary>
/// Chooses which compiled form of <see cref="ShaderModuleId.UpscaleRejectShading"/> an adapter can run.
/// </summary>
/// <remarks>
/// <para>
/// The pass tiles a five-stage chain of 3x3 filters through group-shared memory, and the tiles are the
/// group plus an apron, so their cost grows with the group and not with the frame. At the preferred
/// sixteen-square group that is 39.8 KiB, which fits the 48 KiB a desktop Vulkan adapter reports and not
/// the 32 KiB Direct3D 12 and Metal allow. Exceeding the limit is not a slow path: pipeline creation fails
/// outright, so the choice has to be made before the module is handed to the driver.
/// </para>
/// <para>
/// The forms differ only in the defines the build compiled them with. The measure is identical; a smaller
/// group simply carries a worse apron ratio. Sixteen by eight is the widest group inside 32 KiB, and it
/// loops 38 times a thread over the chain where eight by eight loops 53 -- the eight-square form is what
/// Direct3D 12 ran before, at 2.7 ms a 1440p frame against Vulkan's 1.6.
/// </para>
/// </remarks>
public static class UpscaleRejectShadingVariants
{
    /// <summary>The group edge the pass is written for where shared memory allows it.</summary>
    public const int PreferredGroupSize = 16;

    /// <summary>The height of the wide group, sixteen across, that fits 32 KiB.</summary>
    public const int WideGroupHeight = 8;

    /// <summary>The group edge to fall back to where neither of the others fits.</summary>
    public const int CompactGroupSize = 8;

    /// <summary>Gets the variant built at <see cref="PreferredGroupSize"/> square.</summary>
    public static ShaderVariant Preferred => new("group16");

    /// <summary>Gets the variant built <see cref="PreferredGroupSize"/> wide and <see cref="WideGroupHeight"/> tall.</summary>
    public static ShaderVariant Wide => new("group16x8");

    /// <summary>Gets the variant built at <see cref="CompactGroupSize"/> square.</summary>
    public static ShaderVariant Compact => new("group8");

    /// <summary>Gets how much shared memory the module claims at a given group shape.</summary>
    /// <param name="groupWidth">The group's width, in pixels.</param>
    /// <param name="groupHeight">The group's height, in pixels; the width when left out.</param>
    /// <returns>The total size of the five tiles, in bytes.</returns>
    /// <remarks>
    /// The five tiles the source declares, each a float per channel: two at radius six, one at five and
    /// two at four. <c>UpscaleRejectShadingVariantTests</c> reads the radii back out of the Slang source,
    /// so the two cannot drift apart without a test saying so.
    /// </remarks>
    public static int SharedBytes(int groupWidth, int groupHeight = 0)
    {
        if (groupHeight <= 0)
        {
            groupHeight = groupWidth;
        }

        int Tile(int radius) => (groupWidth + (2 * radius)) * (groupHeight + (2 * radius)) * 3 * sizeof(float);

        return (2 * Tile(6)) + Tile(5) + (2 * Tile(4));
    }

    /// <summary>Chooses the widest variant an adapter's shared memory can hold.</summary>
    /// <param name="maxSharedMemoryBytes">What the adapter reports it allows one group.</param>
    /// <returns>The variant to load, and the group shape it dispatches in.</returns>
    public static (ShaderVariant Variant, int GroupWidth, int GroupHeight) Select(uint maxSharedMemoryBytes)
    {
        if (maxSharedMemoryBytes >= (uint)SharedBytes(PreferredGroupSize))
        {
            return (Preferred, PreferredGroupSize, PreferredGroupSize);
        }

        if (maxSharedMemoryBytes >= (uint)SharedBytes(PreferredGroupSize, WideGroupHeight))
        {
            return (Wide, PreferredGroupSize, WideGroupHeight);
        }

        return (Compact, CompactGroupSize, CompactGroupSize);
    }
}
