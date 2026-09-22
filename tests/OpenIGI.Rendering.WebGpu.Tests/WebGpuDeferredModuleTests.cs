using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// That the deferred group's WGSL is accepted by a real implementation, and its layout with it.
/// </summary>
/// <remarks>
/// Emitting WGSL is not the same as emitting WGSL that validates, and the difference is the whole
/// reason this runs against wgpu rather than against a reading of the text. A bind group layout is
/// checked the same way: it is built from the module's own reflection and handed to the device, which
/// is where a texture declared with the wrong dimension or a sampler with the wrong kind is refused.
/// </remarks>
public sealed class WebGpuDeferredModuleTests
{
    [WebGpuFact]
    public void The_shading_compiles_and_its_group_is_accepted() =>
        Accepts(ShaderModuleId.Shading, new ShaderVariant("software.sampled"));

    [WebGpuFact]
    public void The_resolve_compiles_and_its_group_is_accepted() =>
        Accepts(ShaderModuleId.Resolve, ShaderVariant.Default);

    private static void Accepts(ShaderModuleId module, ShaderVariant variant)
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        using IRhiShaderModule compiled = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(module, variant, module.ToString()));

        ShaderReflection reflection = ShaderCatalog.GetReflection(module, variant);

        using IRhiBindGroupLayout layout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            ShaderBindingLayout.EntriesFor(reflection, RhiShaderStages.Fragment),
            module.ToString()));

        Assert.NotNull(layout);
    }
}
