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
        // The traced surface-cache lighting has a SPIR-V form alone; WebGPU cannot run it.
        using RecordingRhiDevice rhi = new() { ShaderTarget = ShaderTarget.Wgsl };
        using GraphicsDevice device = Open(rhi);

        Assert.Throws<GraphicsDeviceException>(() =>
            device.CreateComputeShader(ShaderModuleId.CardsLighting, new ShaderVariant("rayquery"), "cards.lighting"));
    }

    [Fact]
    public void A_WGSL_dispatch_reads_what_each_read_back_image_held_from_a_copy_taken_first()
    {
        // The radiance cache eases each probe onto what it held. WGSL cannot read an rgba16float image,
        // so each of the four volumes is copied before the pass and the copy bound at its history slot.
        using RecordingRhiDevice rhi = new() { ShaderTarget = ShaderTarget.Wgsl };
        using GraphicsDevice device = Open(rhi);

        ComputeShaderHandle cache = RadianceCache(device);
        device.Dispatch(cache, 1, 1, 1);

        string[] copied = [.. rhi.CommandsOf<RecordedRhiCommand.CopyTextureToTexture>().Select(copy => copy.Source)];
        Assert.Equal(["red", "green", "blue", "distance"], copied);

        // Every copy lands before the pass that reads it.
        List<RecordedRhiCommand> commands = [.. rhi.Commands];
        int pass = commands.FindIndex(command => command is RecordedRhiCommand.BeginComputePass);
        Assert.All(
            commands.OfType<RecordedRhiCommand.CopyTextureToTexture>(),
            copy => Assert.True(commands.IndexOf(copy) < pass));

        RecordedBindGroup group = rhi.Resources.OfType<RecordedBindGroup>().Last();
        Assert.Contains(group.Entries, entry => entry.Binding == 44 && entry.TextureView?.Label == "red.history");
        Assert.Contains(group.Entries, entry => entry.Binding == 47 && entry.TextureView?.Label == "distance.history");
    }

    [Fact]
    public void A_SPIR_V_dispatch_reads_its_images_back_itself_and_copies_nothing()
    {
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        ComputeShaderHandle cache = RadianceCache(device);
        device.Dispatch(cache, 1, 1, 1);

        Assert.Empty(rhi.CommandsOf<RecordedRhiCommand.CopyTextureToTexture>());

        RecordedBindGroup group = rhi.Resources.OfType<RecordedBindGroup>().Last();
        Assert.DoesNotContain(
            group.Entries,
            entry => entry.TextureView?.Label.EndsWith(".history", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void A_unit_still_holding_the_image_a_dispatch_writes_reads_a_stand_in()
    {
        // The bounce gather writes the indirect atlas while the unit the combine samples it through still
        // holds it. WebGPU refuses the pair in one dispatch; the gather never samples it.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        ComputeShaderHandle lighting = device.CreateComputeShader(
            ShaderModuleId.CardsLighting,
            new ShaderVariant("software"),
            "cards.lighting");

        TextureHandle atlas = device.CreateStorageTexture(16, 16, VolumeFormat.Rgba16F, "atlas");
        device.BindImage(0, atlas, ImageAccess.ReadWrite);
        device.SetTexture(7, atlas);

        StorageBufferHandle buffer = device.CreateStorageBuffer(256, "buffer");
        for (int slot = 0; slot < 7; slot++)
        {
            device.BindStorageBuffer(slot, buffer);
        }

        device.Dispatch(lighting, 1);

        RecordedBindGroup group = rhi.Resources.OfType<RecordedBindGroup>().Last();
        Assert.Contains(group.Entries, entry => entry.Binding == 0 && entry.TextureView?.Label == "atlas");
        Assert.Contains(group.Entries, entry => entry.Binding == 39 && entry.TextureView?.Label == "compute.standin.flat");
    }

    [Fact]
    public void A_unit_holding_a_texture_of_the_wrong_kind_for_the_slot_reads_a_stand_in_of_the_right_one()
    {
        // A colour texture left on the unit a depth slot reads: WebGPU refuses it, and no read of it could
        // have meant anything. The slot gets a depth stand-in, which a colour one would be refused as too.
        using RecordingRhiDevice rhi = new();
        using GraphicsDevice device = Open(rhi);

        ComputeShaderHandle resolve = device.CreateComputeShader(
            ShaderModuleId.CardsScreenResolve,
            ShaderVariant.Default,
            "cards.screen.resolve");

        device.BindImage(0, device.CreateStorageTexture(16, 16, VolumeFormat.Rgba16F, "accum"), ImageAccess.Write);
        device.BindImage(1, device.CreateStorageTexture(16, 16, VolumeFormat.Rgba16F, "position"), ImageAccess.Write);
        device.SetTexture(5, device.CreateStorageTexture(16, 16, VolumeFormat.Rgba16F, "colour"));
        device.SetComputeUniform(resolve, "uSceneDepth", 5);

        device.Dispatch(resolve, 1, 1);

        RecordedBindGroup group = rhi.Resources.OfType<RecordedBindGroup>().Last();
        Assert.Contains(group.Entries, entry => entry.Binding == 33 && entry.TextureView?.Label == "compute.standin.depth");
    }

    private static GraphicsDevice Open(RecordingRhiDevice rhi) => new(rhi, new DisplayMode(64, 64, 32, 60));

    /// <summary>The radiance cache in software with its four volumes and its buffers bound.</summary>
    private static ComputeShaderHandle RadianceCache(GraphicsDevice device)
    {
        ComputeShaderHandle cache = device.CreateComputeShader(
            ShaderModuleId.CardsRadianceCache,
            new ShaderVariant("software"),
            "cards.radiance");

        string[] volumes = ["red", "green", "blue", "distance"];
        for (int slot = 0; slot < volumes.Length; slot++)
        {
            device.BindImage(slot, device.CreateVolumeTexture(4, 4, 4, VolumeFormat.Rgba16F, volumes[slot]), ImageAccess.ReadWrite);
        }

        StorageBufferHandle buffer = device.CreateStorageBuffer(256, "buffer");
        for (int slot = 0; slot < 8; slot++)
        {
            device.BindStorageBuffer(slot, buffer);
        }

        return cache;
    }

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
