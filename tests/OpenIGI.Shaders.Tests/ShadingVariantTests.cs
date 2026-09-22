using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// That the sampled variant of the shading is the same shader reading the same things.
/// </summary>
/// <remarks>
/// <para>
/// A subpass input is the attachment the pass before wrote, read at this very pixel and nowhere else,
/// which on a tiler never leaves the tile. It is also a thing only Vulkan has: WebGPU has no subpasses,
/// so the renderer built over the RHI reads the same texels as ordinary textures from a second pass.
/// </para>
/// <para>
/// The two have to shade identically, and the way to be sure of that is that they are one source with
/// one line different. What this checks is that nothing else drifted: same bindings, same numbers, same
/// kinds, same constants — with exactly the five G-buffer inputs changing what they are.
/// </para>
/// </remarks>
public sealed class ShadingVariantTests
{
    private static readonly string[] GBuffer =
        ["uBaseColour", "uNormal", "uMaterial", "uBaked", "uDepth"];

    [Theory]
    [InlineData("rayquery")]
    [InlineData("software")]
    public void The_sampled_variant_binds_what_the_subpass_one_binds(string variant)
    {
        ShaderReflection subpass = ShaderCatalog.GetReflection(ShaderModuleId.Shading, new ShaderVariant(variant));

        ShaderReflection sampled = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant(variant + ".sampled"));

        Assert.Equal(subpass.Bindings.Count, sampled.Bindings.Count);

        for (int i = 0; i < subpass.Bindings.Count; i++)
        {
            ShaderBinding before = subpass.Bindings[i];
            ShaderBinding after = sampled.Bindings[i];

            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Group, after.Group);
            Assert.Equal(before.Binding, after.Binding);

            // The five that change, and nothing else.
            if (GBuffer.Contains(before.Name))
            {
                Assert.Equal(ShaderBindingKind.InputAttachment, before.Kind);
                Assert.Equal(ShaderBindingKind.Texture, after.Kind);
            }
            else
            {
                Assert.Equal(before.Kind, after.Kind);
            }
        }
    }

    [Theory]
    [InlineData("rayquery")]
    [InlineData("software")]
    public void The_sampled_variant_keeps_the_same_push_block_and_constants(string variant)
    {
        ShaderReflection subpass = ShaderCatalog.GetReflection(ShaderModuleId.Shading, new ShaderVariant(variant));

        ShaderReflection sampled = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant(variant + ".sampled"));

        Assert.Equal(subpass.PushBlock?.Size, sampled.PushBlock?.Size);
        Assert.Equal(subpass.SpecializationConstants.Count, sampled.SpecializationConstants.Count);

        Assert.Equal(
            subpass.EntryPoints.Select(entry => entry.Name),
            sampled.EntryPoints.Select(entry => entry.Name));
    }

    [Fact]
    public void The_subpass_variant_is_still_the_one_a_module_gets_by_default()
    {
        // The path that has always drawn this frame is the one that needs nothing said about it.
        ShaderReflection defaulted = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant("software"));

        Assert.Contains(defaulted.Bindings, binding => binding.Kind is ShaderBindingKind.InputAttachment);
    }

    [Fact]
    public void The_sampled_variant_cannot_reach_the_browser_until_its_samplers_are_split()
    {
        // Stated rather than assumed: the shading declares a dozen combined image samplers, which WGSL
        // has no form for and the RHI therefore has no binding type for. Splitting them is what the
        // deferred group's own migration has to do, and until it does this variant is Vulkan's alone.
        ShaderReflection sampled = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant("software.sampled"));

        Assert.Contains(sampled.Bindings, binding => binding.Kind is ShaderBindingKind.CombinedImageSampler);
    }
}
