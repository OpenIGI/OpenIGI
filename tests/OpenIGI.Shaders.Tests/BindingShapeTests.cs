using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// That the library records the two things about a binding only the browser can tell are wrong.
/// </summary>
/// <remarks>
/// A WebGPU bind group layout states, for a texture, whether it is an array of slices, and for a
/// sampler, whether it compares rather than filters — and it is validated against both. A Vulkan
/// descriptor set distinguishes neither: an arrayed texture and a flat one are the same sampled image,
/// and a comparison sampler and an ordinary one are the same sampler. So a module that got either wrong
/// would draw a correct frame here for as long as anyone looked and bind nothing in the browser.
/// </remarks>
public sealed class BindingShapeTests
{
    [Fact]
    public void The_cascades_are_an_array_read_through_a_comparison_sampler()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant("software.sampled"));

        ShaderBinding map = Single(reflection, "uShadowMap");
        ShaderBinding sampler = Single(reflection, "uShadowMapSampler");

        Assert.True(map.Arrayed);
        Assert.Equal(2, map.Dimensions);
        Assert.False(map.Comparison);

        Assert.True(sampler.Comparison);
    }

    [Fact]
    public void An_arrayed_texture_is_known_from_a_variant_with_no_wgsl_form()
    {
        // Whether a texture is arrayed comes from Slang, so it is recorded for a Vulkan-only variant
        // too. Read from the WGSL instead, the traced forms of the shading would carry nothing.
        ShaderReflection traced = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant("rayquery"));

        Assert.True(Single(traced, "uShadowMap").Arrayed);
    }

    [Fact]
    public void An_ordinary_texture_and_an_ordinary_sampler_say_neither()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Resolve);

        Assert.False(Single(reflection, "uScene").Arrayed);
        Assert.False(Single(reflection, "uSceneSampler").Comparison);

        // And a volume is three dimensions rather than an array of two.
        ShaderBinding lut = Single(reflection, "uColourLut");
        Assert.Equal(3, lut.Dimensions);
        Assert.False(lut.Arrayed);
    }

    [Fact]
    public void The_shading_and_the_resolve_now_have_a_browser_form()
    {
        // What the split was for: both were compiled for Vulkan alone while they declared combined
        // image samplers, which WGSL has no type for.
        Assert.True(Compiles(ShaderModuleId.Resolve, ShaderVariant.DefaultName, ShaderTarget.Wgsl));
        Assert.True(Compiles(ShaderModuleId.Shading, "software.sampled", ShaderTarget.Wgsl));

        // The subpass form stays Vulkan's: WebGPU has no subpasses, which is why the sampled one exists.
        Assert.False(Compiles(ShaderModuleId.Shading, "software", ShaderTarget.Wgsl));
        Assert.True(Compiles(ShaderModuleId.Shading, "software", ShaderTarget.Spirv));
    }

    private static bool Compiles(ShaderModuleId module, string variant, ShaderTarget target) =>
        ShaderCatalog.Manifest.Modules
            .Single(entry => entry.Id == module)
            .Variants
            .Single(entry => entry.Name == variant)
            .Compiles(target);

    private static ShaderBinding Single(ShaderReflection reflection, string name) =>
        Assert.Single(reflection.Bindings, binding => binding.Name == name);
}
