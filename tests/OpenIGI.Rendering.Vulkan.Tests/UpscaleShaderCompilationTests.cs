using System.Globalization;
using System.Text.RegularExpressions;
using OpenIGI.Rendering.Vulkan.Deferred.Tsr;
using OpenIGI.Rendering.Vulkan.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// Compiles the temporal-resolve compute shaders through the compiler the backend itself uses.
/// </summary>
/// <remarks>
/// <para>
/// These sources are assembled from string constants, so nothing at build time reads them: a mistake in
/// one is found by the first frame that dispatches it, on whatever machine happens to run next. That is
/// a poor place to find it. <see cref="ShaderCompilationTests"/> makes the same point for the shelter-map
/// shaders, but needs a <c>glslc</c> on PATH and skips without one; this needs nothing but the shaderc
/// the renderer already links, so it runs everywhere the suite does.
/// </para>
/// <para>
/// The reject-shading shader is here for a specific reason. It carries six shared tiles sized to fit a
/// workgroup's 48KB, and that arithmetic is only correct while each cell occupies twelve bytes -- which
/// is why the cells are three floats rather than a vec3, whose stride the driver picks. A change that
/// reintroduced a vec3 tile would still compile, so the size is asserted here alongside it.
/// </para>
/// </remarks>
public sealed class UpscaleShaderCompilationTests
{
    [RequiresShaderCompilerFact]
    public void TheRejectShadingShaderCompiles()
    {
        using ShaderCompiler compiler = new();

        byte[] spirv = compiler.Compile(
            UpscaleRejectShadingShaderSource.Compute, ShaderStage.Compute, "upscale.rejectshading.comp");

        Assert.NotEmpty(spirv);
    }

    /// <summary>The narrow group an adapter without 48KB of shared memory gets is built the same way.</summary>
    /// <remarks>
    /// Only the group size differs, but everything downstream of it is derived: seven tile extents, the
    /// stride of every cooperative loop and the group's own origin. A mistake in any of them is a source
    /// no adapter that needs this path can compile, and no desktop driver would ever ask for it.
    /// </remarks>
    [RequiresShaderCompilerFact]
    public void TheRejectShadingShaderCompilesAtTheCompactGroupSize()
    {
        using ShaderCompiler compiler = new();

        byte[] spirv = compiler.Compile(
            UpscaleRejectShadingShaderSource.For(UpscaleRejectShadingShaderSource.CompactGroupSize),
            ShaderStage.Compute,
            "upscale.rejectshading.comp");

        Assert.NotEmpty(spirv);
    }

    /// <summary>
    /// The shared-memory total the pass decides on is the total the source actually declares.
    /// </summary>
    /// <remarks>
    /// <see cref="RejectShadingPass"/> chooses its group size by comparing
    /// <see cref="UpscaleRejectShadingShaderSource.SharedBytes"/> against the adapter's
    /// <c>maxComputeSharedMemorySize</c>, so that arithmetic is load-bearing rather than documentation:
    /// under-count it and the pipeline is rejected on the adapter the fallback exists for. It is
    /// recomputed here from the declarations themselves -- the six tiles and the radii they are sized
    /// from -- so that adding a tile or widening a radius fails here rather than on a Mac.
    /// </remarks>
    [Fact]
    public void TheRejectShadingSharedTotalMatchesTheDeclaredTiles()
    {
        string source = UpscaleRejectShadingShaderSource.Compute;

        // "const int R_LOAD = 6; const int T_LOAD = GROUP + 12;" -- the radius, and the extent it implies.
        Dictionary<string, int> radii = [];
        foreach (Match match in Regex.Matches(source, @"const int R_(\w+)\s*=\s*(\d+);\s*const int T_\1\s*=\s*GROUP \+ (\d+);"))
        {
            int radius = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.Equal(2 * radius, int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            radii[match.Groups[1].Value] = radius;
        }

        Assert.NotEmpty(radii);

        // "shared float sInput[T_LOAD * T_LOAD * 3];" -- which of those extents is actually claimed.
        MatchCollection tiles = Regex.Matches(source, @"shared float \w+\[T_(\w+) \* T_\1 \* 3\];");
        Assert.Equal(6, tiles.Count);

        foreach (int groupSize in new[] { UpscaleRejectShadingShaderSource.PreferredGroupSize, UpscaleRejectShadingShaderSource.CompactGroupSize })
        {
            int declared = 0;
            foreach (Match tile in tiles)
            {
                int edge = groupSize + (2 * radii[tile.Groups[1].Value]);
                declared += edge * edge * 3 * sizeof(float);
            }

            Assert.Equal(declared, UpscaleRejectShadingShaderSource.SharedBytes(groupSize));
        }

        // The limit the fallback exists for: Metal's threadgroup memory on Apple silicon, which MoltenVK
        // reports verbatim. The compact tile has to fit inside it or there is nothing to fall back to.
        Assert.True(UpscaleRejectShadingShaderSource.SharedBytes(UpscaleRejectShadingShaderSource.CompactGroupSize) <= 32768);
    }

    /// <summary>
    /// Every shared tile is a float array, so its stride is four bytes rather than the driver's choice.
    /// </summary>
    /// <remarks>
    /// A <c>shared vec3</c> array may be laid out with a sixteen-byte stride, which takes this shader's
    /// six tiles from 46.5KB to 62KB -- past the 48KB a workgroup may claim. Ampere may be granted the
    /// larger carveout and run it anyway; Pascal cannot, and a GTX 1060 lost its device in this pass.
    /// The declarations are asserted rather than the byte total because the total is only knowable once
    /// a driver has chosen a layout, and the point of the float arrays is that it no longer gets to.
    /// </remarks>
    [Fact]
    public void TheRejectShadingSharedTilesAreFloatArrays()
    {
        string[] declarations = [.. UpscaleRejectShadingShaderSource.Compute
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("shared ", StringComparison.Ordinal))];

        Assert.Equal(6, declarations.Length);
        Assert.All(declarations, line => Assert.StartsWith("shared float ", line, StringComparison.Ordinal));
        Assert.All(declarations, line => Assert.EndsWith("* 3];", line, StringComparison.Ordinal));
    }
}

/// <summary>A <see cref="FactAttribute"/> that skips when shaderc's native library will not load.</summary>
/// <remarks>
/// <para>
/// This was a plain <see cref="FactAttribute"/>, on the reasoning that the renderer links shaderc
/// anyway so the compiler is always there. It is not: a machine with no graphics stack — continuous
/// integration, most obviously — has nothing for the native loader to find, and the test failed with
/// <c>Could not load from any of the possible library names</c> on every Linux run while passing on
/// every developer's box.
/// </para>
/// <para>
/// A shader that will not compile must still fail loudly wherever a compiler exists, so this skips on
/// the library being absent and on nothing else.
/// </para>
/// </remarks>
public sealed class RequiresShaderCompilerFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequiresShaderCompilerFactAttribute"/> class.</summary>
    public RequiresShaderCompilerFactAttribute()
    {
        if (!ShaderCompilerProbe.IsAvailable)
        {
            Skip = "shaderc's native library is not loadable here, so the shaders cannot be compiled in a test.";
        }
    }
}

/// <summary>Whether shaderc can be loaded at all, asked once.</summary>
internal static class ShaderCompilerProbe
{
    /// <summary>Gets a value indicating whether a compiler could be created.</summary>
    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using ShaderCompiler compiler = new();
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or FileNotFoundException
            or EntryPointNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            return false;
        }
    }
}
