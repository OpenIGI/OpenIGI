using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using OpenIGI.TestSupport.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That the game's compute seam lands on the module's own slots: images, buffers and textures by the
/// binding space every module keeps to, parameters by name, and a texture the parameters name read
/// through the unit they say.
/// </summary>
public sealed class ComputeSeamTests
{
    [Fact]
    public void A_dispatch_fills_every_slot_from_what_the_seam_bound()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        ComputeShaderHandle filter = Filter(device, out TextureHandle filtered, out TextureHandle accumulated);

        device.SetComputeUniform(filter, "uWidth", 16);
        device.SetComputeUniform(filter, "uRadius", 2.5f);
        device.Dispatch(filter, 2, 2);

        Assert.Equal(new RecordedRhiCommand.Dispatch(2, 2, 1), Assert.Single(rhi.CommandsOf<RecordedRhiCommand.Dispatch>()));

        RecordedBindGroup group = rhi.Resources.OfType<RecordedBindGroup>().Last();

        // The image at slot 0 and, because the parameters name it, the texture on the unit uAccum was set to.
        Assert.Contains(group.Entries, entry => entry.Binding == 0 && entry.TextureView?.Label == "filtered");
        Assert.Contains(group.Entries, entry => entry.Binding == 32 && entry.TextureView?.Label == "accumulated");

        // The parameters, where the block says they are; the unit is not one of them.
        RecordedBuffer globals = Assert.Single(rhi.Resources.OfType<RecordedBuffer>(), buffer => buffer.Label.EndsWith(".globals", StringComparison.Ordinal));
        Assert.Equal(16, BitConverter.ToInt32(globals.Contents, 0));
        Assert.Equal(2.5f, BitConverter.ToSingle(globals.Contents, 8));
        Assert.Equal(0, BitConverter.ToInt32(globals.Contents, 12));

        GC.KeepAlive((filtered, accumulated));
    }

    [Fact]
    public void Each_dispatch_of_a_frame_keeps_the_parameters_it_was_given()
    {
        // The game sets a shader's parameters and dispatches it several times a frame. One block written
        // in place would give every dispatch of the frame the last one's values.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        ComputeShaderHandle filter = Filter(device, out _, out _);

        OpenFrame(device);

        device.SetComputeUniform(filter, "uWidth", 1);
        device.Dispatch(filter, 1, 1);
        device.SetComputeUniform(filter, "uWidth", 2);
        device.Dispatch(filter, 1, 1);

        int[] widths = [.. rhi.Resources.OfType<RecordedBuffer>()
            .Where(buffer => buffer.Label.EndsWith(".globals", StringComparison.Ordinal))
            .Select(buffer => BitConverter.ToInt32(buffer.Contents, 0))];

        Assert.Equal([1, 2], widths);
    }

    [Fact]
    public void A_slot_the_module_declares_and_the_seam_left_empty_is_refused_by_name()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        ComputeShaderHandle build = device.CreateComputeShader(ShaderModuleId.FieldClipmapBuild, ShaderVariant.Default, "field.build");
        device.BindImage(0, device.CreateVolumeTexture(4, 4, 4, VolumeFormat.R8, "clipmaps"), ImageAccess.Write);

        GraphicsDeviceException refused = Assert.Throws<GraphicsDeviceException>(() => device.Dispatch(build, 1, 1, 1));

        Assert.Contains("uInstances", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_with_no_form_for_the_backend_is_refused_when_it_is_created()
    {
        // The surface cache's lighting has a SPIR-V form alone, even in software; WebGPU cannot run it.
        using RecordingRhiDevice rhi = new() { ShaderTarget = ShaderTarget.Wgsl };
        using GraphicsDevice device = Open(rhi);

        Assert.Throws<GraphicsDeviceException>(() =>
            device.CreateComputeShader(ShaderModuleId.CardsLighting, new ShaderVariant("software"), "cards.lighting"));
    }

    private static GraphicsDevice Open(RecordingRhiDevice rhi) => new(rhi, new DisplayMode(64, 64, 32, 60));

    /// <summary>The screen filter with everything bound, its accumulation read through unit one.</summary>
    private static ComputeShaderHandle Filter(GraphicsDevice device, out TextureHandle filtered, out TextureHandle accumulated)
    {
        ComputeShaderHandle filter = device.CreateComputeShader(
            ShaderModuleId.CardsScreenFilter,
            ShaderVariant.Default,
            "cards.screen.filter");

        filtered = device.CreateStorageTexture(16, 16, VolumeFormat.Rgba16F, "filtered");
        accumulated = device.CreateStorageTexture(16, 16, VolumeFormat.Rgba16F, "accumulated");

        device.BindImage(0, filtered, ImageAccess.Write);
        device.SetTexture(1, accumulated);
        device.SetComputeUniform(filter, "uAccum", 1);
        return filter;
    }

    /// <summary>Opens a frame the way the game does, by drawing into it.</summary>
    private static void OpenFrame(GraphicsDevice device)
    {
        MeshHandle mesh = device.CreateMesh(
            [
                new Vertex3D(new Vector3f(0f, 0f, 0f), Vector3f.UnitY, 0f, 0f),
                new Vertex3D(new Vector3f(1f, 0f, 0f), Vector3f.UnitY, 1f, 0f),
                new Vertex3D(new Vector3f(1f, 1f, 0f), Vector3f.UnitY, 1f, 1f),
            ],
            [0, 1, 2]);

        device.DrawMesh(mesh, 0, 3, Matrix4f.Identity, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);
    }
}
