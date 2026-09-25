using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Shaders;
using Silk.NET.Vulkan;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// That the deferred set layouts are the shaders' own binding tables and not a copy of them.
/// </summary>
/// <remarks>
/// A descriptor set laid out differently from the shader that reads it is not reported by anything:
/// the frame shades with whatever was in the slot the descriptor landed in. These check the derivation
/// rather than the numbers, because the numbers are the shader's to choose.
/// </remarks>
public sealed class ReflectedSetLayoutTests
{
    [Theory]
    [InlineData("rayquery")]
    [InlineData("software")]
    public void Every_binding_the_shading_declares_is_counted_for_the_pool(string variant)
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant(variant));

        Dictionary<DescriptorType, uint> counts = ReflectedSetLayout.Counts(reflection);

        Assert.Equal(
            (uint)reflection.Bindings.Count(binding => binding.Group == 0),
            (uint)counts.Values.Sum(count => count));
    }

    [Fact]
    public void A_texture_and_its_sampler_are_counted_as_two_kinds_of_descriptor()
    {
        // The whole point of the split: a combined sampler was one descriptor, and the pool that fed the
        // old layout had no sampler descriptors in it at all.
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Resolve, ShaderVariant.Default);

        Dictionary<DescriptorType, uint> counts = ReflectedSetLayout.Counts(reflection);

        Assert.Equal(3u, counts[DescriptorType.SampledImage]);
        Assert.Equal(3u, counts[DescriptorType.Sampler]);
        Assert.False(counts.ContainsKey(DescriptorType.CombinedImageSampler));
    }

    [Fact]
    public void The_traced_shading_declares_what_the_software_one_does_not()
    {
        // The conditional half of the layout, which used to be two hand-counted branches.
        Dictionary<DescriptorType, uint> traced = ReflectedSetLayout.Counts(
            ShaderCatalog.GetReflection(ShaderModuleId.Shading, new ShaderVariant("rayquery")));

        Dictionary<DescriptorType, uint> software = ReflectedSetLayout.Counts(
            ShaderCatalog.GetReflection(ShaderModuleId.Shading, new ShaderVariant("software")));

        // Only the world itself. The lamp visibility used to be the traced form's alone; the software form
        // reads it too now, from the distance-field lamp trace a renderer without ray tracing runs.
        Assert.Equal(1u, traced[DescriptorType.AccelerationStructureKhr]);
        Assert.False(software.ContainsKey(DescriptorType.AccelerationStructureKhr));
        Assert.Equal(software[DescriptorType.SampledImage], traced[DescriptorType.SampledImage]);
    }

    [Fact]
    public void The_g_buffer_stays_an_input_attachment_where_the_subpass_reads_it()
    {
        // Five attachments read at their own pixel, which is a thing only Vulkan has and the reason the
        // sampled variants exist at all.
        Dictionary<DescriptorType, uint> counts = ReflectedSetLayout.Counts(
            ShaderCatalog.GetReflection(ShaderModuleId.Shading, new ShaderVariant("software")));

        Assert.Equal(5u, counts[DescriptorType.InputAttachment]);
    }

    [Fact]
    public void A_binding_with_no_single_descriptor_form_is_refused()
    {
        Assert.Throws<NotSupportedException>(
            () => ReflectedSetLayout.TypeOf(ShaderBindingKind.AccelerationStructureArray));
    }

    [Fact]
    public void A_name_the_shader_does_not_declare_says_which_shader_it_asked()
    {
        ShaderBindingTable table = new(
            ShaderCatalog.GetReflection(ShaderModuleId.Shading, new ShaderVariant("software")));

        Assert.False(table.Declares("uWorldTlas"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => table["uWorldTlas"]);
        Assert.Contains("Shading", error.Message, StringComparison.Ordinal);
        Assert.Contains("software", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_table_gives_back_the_slot_the_shader_declares()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(
            ShaderModuleId.Shading,
            new ShaderVariant("rayquery"));

        ShaderBindingTable table = new(reflection);

        foreach (ShaderBinding binding in reflection.Bindings.Where(b => b.Group == 0))
        {
            Assert.Equal((uint)binding.Binding, table[binding.Name]);
        }
    }
}
