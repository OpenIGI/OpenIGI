using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// That a specialisation constant actually reaches a pipeline on this backend.
/// </summary>
/// <remarks>
/// <para>
/// Worth its own tests because the interface carried them for a while and neither backend honoured
/// them: the descriptor said what the constants were, the pipeline was built without them, and every
/// shader shaded with whatever its default was. Nothing failed. The way that surfaced was a picture
/// that came out upside down, which is one of the few constants whose absence is visible at all.
/// </para>
/// <para>
/// Here the constant is keyed by NAME rather than by number, which is the one place the two backends
/// genuinely differ about what a constant is — so a name that the module does not declare has to be
/// refused with something a reader can act on rather than passed to the driver.
/// </para>
/// </remarks>
public sealed class WebGpuConstantTests(ITestOutputHelper output)
{
    [WebGpuFact]
    public void A_module_knows_the_name_it_declares_a_constant_under()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        using IRhiShaderModule module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(ShaderModuleId.Mesh3D, Label: "Mesh3D"));

        WebGpuShaderModule mesh = Assert.IsType<WebGpuShaderModule>(module);

        Assert.NotEmpty(mesh.Reflection.SpecializationConstants);

        foreach (ShaderSpecializationConstant constant in mesh.Reflection.SpecializationConstants)
        {
            output.WriteLine($"{constant.Id}: {constant.Name}");
            Assert.Equal(constant.Name, mesh.ConstantName(constant.Id));
        }
    }

    [WebGpuFact]
    public void A_constant_the_module_does_not_declare_is_refused_by_name()
    {
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();

        using IRhiShaderModule module = device.CreateShaderModule(
            new RhiShaderModuleDescriptor(ShaderModuleId.Mesh3D, Label: "Mesh3D"));

        WebGpuShaderModule mesh = Assert.IsType<WebGpuShaderModule>(module);

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => mesh.ConstantName(9999));

        Assert.Contains("9999", refused.Message, StringComparison.Ordinal);
    }

    [WebGpuFact]
    public void A_pipeline_built_with_constants_is_accepted_by_the_driver()
    {
        // The proof that the names and the values are the ones wgpu wants: a key the module does not
        // declare, or a value of the wrong type for what it declares, fails pipeline creation. Built
        // through the cache the renderer itself uses, so the layout is the real one rather than a guess.
        using WebGpuRhiDevice device = WebGpuRhiDevice.Create();
        using IRhiBindGroupLayout material = Mesh3DPipelineCache.CreateMaterialLayout(device);

        using ShadowPipelineCache shadows = new(device, material, RhiTextureFormat.Depth32Float);

        IRhiRenderPipeline pipeline = shadows.ForAlphaReference(200);

        Assert.NotNull(pipeline);
        output.WriteLine($"built {shadows.Count} variant(s) with constants");
    }
}
