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
    [Fact]
    public void TheRejectShadingShaderCompiles()
    {
        using ShaderCompiler compiler = new();

        byte[] spirv = compiler.Compile(
            UpscaleRejectShadingShaderSource.Compute, ShaderStage.Compute, "upscale.rejectshading.comp");

        Assert.NotEmpty(spirv);
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
