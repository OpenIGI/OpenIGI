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

    [Theory]
    [InlineData("rayquery")]
    [InlineData("software")]
    [InlineData("rayquery.sampled")]
    [InlineData("software.sampled")]
    public void The_shading_declares_its_textures_and_samplers_apart(string variant)
    {
        // WGSL has no combined image sampler, and a module that declares one cannot be compiled for the
        // browser at all -- so the deferred group declares each texture and the sampler it is read with
        // in two slots of its own, which is the only shape both targets share. Checked on the subpass
        // variants too: they are one source, and a combined sampler reintroduced for Vulkan's
        // convenience would take the sampled variants with it.
        ShaderReflection reflection = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant(variant));

        Assert.DoesNotContain(
            reflection.Bindings,
            binding => binding.Kind is ShaderBindingKind.CombinedImageSampler);
    }

    [Theory]
    [InlineData("rayquery")]
    [InlineData("software")]
    [InlineData("rayquery.sampled")]
    [InlineData("software.sampled")]
    public void Each_of_the_shadings_samplers_sits_in_the_slot_after_its_texture(string variant)
    {
        // The convention the whole library follows and every host binds by: texture n, sampler n + 1.
        // A sampler that drifted away from its texture would still bind -- and would be handed to the
        // wrong one.
        ShaderReflection reflection = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant(variant));

        foreach (ShaderBinding sampler in reflection.Bindings.Where(b => b.Kind is ShaderBindingKind.Sampler))
        {
            Assert.EndsWith("Sampler", sampler.Name, StringComparison.Ordinal);

            ShaderBinding texture = Assert.Single(
                reflection.Bindings,
                b => b.Group == sampler.Group && b.Binding == sampler.Binding - 1);

            Assert.Equal(sampler.Name[..^"Sampler".Length], texture.Name);
        }
    }

    [Fact]
    public void The_resolve_declares_its_textures_and_samplers_apart()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Resolve, ShaderVariant.Default);

        Assert.DoesNotContain(
            reflection.Bindings,
            binding => binding.Kind is ShaderBindingKind.CombinedImageSampler);
    }
}
