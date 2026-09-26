using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The shading-rejection module is built once per group size, and the arithmetic that picks between
/// them has to describe the shared memory the source actually claims.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UpscaleRejectShadingVariants.Select"/> compares <see cref="UpscaleRejectShadingVariants.SharedBytes"/>
/// against the adapter's <c>maxComputeSharedMemorySize</c>, so that arithmetic is load-bearing rather
/// than documentation: under-count it and the pipeline is rejected outright on the very adapter the
/// fallback exists for. It is recomputed here from the declarations themselves -- the six tiles and the
/// radii they are sized from -- so that adding a tile or widening a radius fails here rather than on a
/// Mac.
/// </para>
/// <para>
/// The tiles are checked to be <c>float</c> arrays for the same reason. A <c>groupshared float3</c> array
/// may be laid out with a sixteen-byte stride, which takes the six tiles from 46.5 KiB to 62 KiB -- past
/// the 48 KiB a workgroup may claim. Ampere may be granted the larger carveout and run it anyway; Pascal
/// cannot, and a GTX 1060 lost its device in this pass. The declarations are asserted rather than the
/// byte total because the total is only knowable once a driver has chosen a layout, and the point of the
/// float arrays is that it no longer gets to.
/// </para>
/// </remarks>
public sealed partial class UpscaleRejectShadingVariantTests
{
    private const string SourcePath = "TemporalUpscale/RejectShading.slang";

    /// <summary>The two group sizes the module is compiled at.</summary>
    public static TheoryData<int> GroupSizes =>
        [UpscaleRejectShadingVariants.PreferredGroupSize, UpscaleRejectShadingVariants.CompactGroupSize];

    /// <summary>The shared total the selection decides on is the total the source declares.</summary>
    /// <param name="groupSize">The group edge to total the tiles at.</param>
    [Theory]
    [MemberData(nameof(GroupSizes))]
    public void The_shared_total_matches_the_declared_tiles(int groupSize)
    {
        string source = ShaderRepository.ReadSource(SourcePath);

        // "static const int R_LOAD = 6;" and "static const int T_LOAD = GROUP + 12;" -- the radius, and
        // the extent it implies.
        Dictionary<string, int> radii = [];
        foreach (Match match in RadiusPattern().Matches(source))
        {
            int radius = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.Equal(2 * radius, int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            radii[match.Groups[1].Value] = radius;
        }

        Assert.NotEmpty(radii);

        // "groupshared float sInput[T_LOAD * T_LOAD * 3];" -- which of those extents is actually claimed.
        MatchCollection tiles = TilePattern().Matches(source);
        Assert.Equal(6, tiles.Count);

        int declared = 0;
        foreach (Match tile in tiles)
        {
            int edge = groupSize + (2 * radii[tile.Groups[1].Value]);
            declared += edge * edge * 3 * sizeof(float);
        }

        Assert.Equal(declared, UpscaleRejectShadingVariants.SharedBytes(groupSize));
    }

    /// <summary>The compact form fits the limit the fallback exists for.</summary>
    /// <remarks>
    /// Metal's threadgroup memory on Apple silicon, which MoltenVK reports verbatim. The compact tile
    /// has to fit inside it or there is nothing to fall back to.
    /// </remarks>
    [Fact]
    public void The_compact_form_fits_metals_threadgroup_memory() =>
        Assert.True(UpscaleRejectShadingVariants.SharedBytes(UpscaleRejectShadingVariants.CompactGroupSize) <= 32768);

    /// <summary>Every shared tile is a float array, so its stride is four bytes and not the driver's choice.</summary>
    [Fact]
    public void The_shared_tiles_are_float_arrays()
    {
        string[] declarations = [.. ShaderRepository.ReadSource(SourcePath)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("groupshared ", StringComparison.Ordinal))];

        Assert.Equal(6, declarations.Length);
        Assert.All(declarations, line => Assert.StartsWith("groupshared float ", line, StringComparison.Ordinal));
        Assert.All(declarations, line => Assert.EndsWith("* 3];", line, StringComparison.Ordinal));
    }

    /// <summary>Each variant was compiled at the group size its name claims.</summary>
    /// <param name="groupSize">The group edge.</param>
    /// <remarks>
    /// The define reaches the compiler through the manifest and the dispatch count comes from the C#
    /// constant, so a variant compiled at the wrong size would dispatch a grid that covers the frame at
    /// one size while each group shades a tile of another -- gaps or overlap, and neither would fail to
    /// build. The reflection is what the compiler actually emitted, so it settles it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(GroupSizes))]
    public void Each_variant_was_compiled_at_the_size_it_is_selected_for(int groupSize)
    {
        (ShaderVariant variant, int selected) = UpscaleRejectShadingVariants.Select(
            (uint)UpscaleRejectShadingVariants.SharedBytes(groupSize));

        Assert.Equal(groupSize, selected);

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.UpscaleRejectShading, variant);
        ShaderEntryPoint entryPoint = reflection.EntryPointFor(ShaderStage.Compute);

        Assert.NotNull(entryPoint.WorkgroupSize);
        Assert.Equal(new[] { groupSize, groupSize, 1 }, entryPoint.WorkgroupSize!);
    }

    /// <summary>An adapter one byte short of the preferred tile gets the compact form.</summary>
    /// <remarks>
    /// The boundary rather than a representative value: the whole point of the selection is that
    /// exceeding the limit is a hard pipeline-creation failure and not a slow path, so being off by one
    /// in the comparison is the mistake that would reach a user.
    /// </remarks>
    [Fact]
    public void One_byte_short_of_the_preferred_tile_selects_the_compact_form()
    {
        int preferred = UpscaleRejectShadingVariants.SharedBytes(UpscaleRejectShadingVariants.PreferredGroupSize);

        Assert.Equal(
            UpscaleRejectShadingVariants.Compact,
            UpscaleRejectShadingVariants.Select((uint)preferred - 1).Variant);
        Assert.Equal(
            UpscaleRejectShadingVariants.Preferred,
            UpscaleRejectShadingVariants.Select((uint)preferred).Variant);
    }

    [GeneratedRegex(@"static const int R_(\w+) = (\d+);\s*static const int T_\1 = GROUP \+ (\d+);")]
    private static partial Regex RadiusPattern();

    [GeneratedRegex(@"groupshared float \w+\[T_(\w+) \* T_\1 \* 3\];")]
    private static partial Regex TilePattern();
}
