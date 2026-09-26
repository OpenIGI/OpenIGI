using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// That the shading's two forms read the G-buffer and bind their textures the way both targets can.
/// </summary>
/// <remarks>
/// The two forms differ only in whether lamps trace their shadows; everything they read is declared the
/// same way, and in the only shapes WGSL shares with SPIR-V: the G-buffer as ordinary textures read at
/// the pixel, and each texture beside a sampler of its own.
/// </remarks>
public sealed class ShadingVariantTests
{
    private static readonly string[] GBuffer =
        ["uBaseColour", "uNormal", "uMaterial", "uBaked", "uDepth"];

    [Theory]
    [InlineData("rayquery")]
    [InlineData("software")]
    public void The_g_buffer_is_read_as_textures(string variant)
    {
        // A subpass input is a thing only Vulkan has, and WebGPU has no subpasses: the pass that shades
        // reads the G-buffer the pass before wrote as ordinary textures.
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Shading, new ShaderVariant(variant));

        Assert.DoesNotContain(reflection.Bindings, binding => binding.Kind is ShaderBindingKind.InputAttachment);

        foreach (string name in GBuffer)
        {
            ShaderBinding binding = Assert.Single(reflection.Bindings, b => b.Name == name);
            Assert.Equal(ShaderBindingKind.Texture, binding.Kind);
        }
    }

    [Theory]
    [InlineData("rayquery")]
    [InlineData("software")]
    public void The_shading_declares_its_textures_and_samplers_apart(string variant)
    {
        // WGSL has no combined image sampler, and a module that declares one cannot be compiled for the
        // browser at all -- so the deferred group declares each texture and the sampler it is read with
        // in two slots of its own, which is the only shape both targets share. Checked on the traced form
        // too: they are one source, and a combined sampler reintroduced for Vulkan's convenience would
        // take the browser's form with it.
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
