using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The geometry pass drawing into the real scene targets, on a real device.
/// </summary>
/// <remarks>
/// The first time the moved renderer writes a G-buffer: four attachments of four different formats, a
/// depth buffer, the material group and the per-frame surface block, all stated through the RHI and
/// none of it naming a graphics API. What it proves that the recording device cannot is that the
/// formats in the target table are ones a driver will actually attach together, and that the pass
/// writes the target it says it writes.
/// </remarks>
public sealed class GBufferPassOnVulkanTests(ITestOutputHelper output)
{
    private const int Size = 64;

    [VulkanFact]
    public void A_triangle_reaches_the_base_colour_target()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            byte[] pixels = DrawIntoGBuffer(vulkan!.Rhi);

            output.WriteLine($"centre: {Describe(pixels, Size / 2, Size / 2)}");
            output.WriteLine($"corner: {Describe(pixels, 2, Size - 2)}");

            // The triangle covers the middle and nothing covers the bottom corner.
            Assert.True(Alpha(pixels, Size / 2, Size / 2) > 0, "The geometry pass wrote no base colour.");
            Assert.Equal(0, Alpha(pixels, 2, Size - 2));
        }
    }

    private static string Describe(byte[] pixels, int x, int y)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * 4);
        int offset = (y * row) + (x * 4);
        return $"r{pixels[offset]} g{pixels[offset + 1]} b{pixels[offset + 2]} a{pixels[offset + 3]}";
    }

    private static int Alpha(byte[] pixels, int x, int y)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * 4);
        return pixels[(y * row) + (x * 4) + 3];
    }

    private static byte[] DrawIntoGBuffer(VulkanRhiDevice device)
    {
        using SceneTargetSet targets = new(device, Size, Size);
        using WhiteTextures textures = new(device);
        using IRhiBindGroupLayout material = Mesh3DPipelineCache.CreateMaterialLayout(device);
        using MaterialBindGroupCache materials = new(device, material);

        using IRhiBindGroupLayout surfaceLayout = device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [new RhiBindGroupLayoutEntry(0, RhiBindingType.UniformBuffer, RhiShaderStages.Graphics)],
            "surface"));

        using IRhiBuffer surface = device.CreateBuffer(new RhiBufferDescriptor(
            256,
            RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
            "surface"));

        device.WriteBuffer<byte>(surface, 0, new byte[256]);

        using IRhiBindGroup surfaceGroup = device.CreateBindGroup(new RhiBindGroupDescriptor(
            surfaceLayout,
            [RhiBindGroupEntry.Of(0, surface)],
            "surface"));

        using GBufferPipelineCache pipelines = new(
            device,
            material,
            surfaceLayout,
            [
                targets[SceneTargetId.BaseColour].Format,
                targets[SceneTargetId.Normal].Format,
                targets[SceneTargetId.Material].Format,
                targets[SceneTargetId.Baked].Format,
            ],
            targets[SceneTargetId.Depth].Format);

        Mesh3DPass meshes = new(textures, materials);

        // In clip space already, so an identity transform leaves it where it is said to be.
        using RhiMesh mesh = RhiMesh.CreateStatic(
            device,
            [
                new Vertex3D(new Vector3f(-0.6f, -0.6f, 0f), Vector3f.UnitZ, 0f, 0f),
                new Vertex3D(new Vector3f(0.6f, -0.6f, 0f), Vector3f.UnitZ, 1f, 0f),
                new Vertex3D(new Vector3f(0f, 0.6f, 0f), Vector3f.UnitZ, 0.5f, 1f),
            ],
            [0, 1, 2],
            "triangle");

        int row = RhiBufferTextureCopy.AlignRow(Size * 4);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "gbuffer.readback"));

        // The model-view-projection, and nothing else the pass needs for a flat white triangle.
        float[] push = new float[GBufferPipelineCache.PushDataBytes / sizeof(float)];
        Matrix4f.Identity.CopyColumnMajorTo(push.AsSpan(0, 16));

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("gbuffer"))
        {
            using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [
                    new RhiColorAttachment(targets.View(SceneTargetId.BaseColour)),
                    new RhiColorAttachment(targets.View(SceneTargetId.Normal)),
                    new RhiColorAttachment(targets.View(SceneTargetId.Material)),
                    new RhiColorAttachment(targets.View(SceneTargetId.Baked)),
                ],
                new RhiDepthAttachment(targets.View(SceneTargetId.Depth), DepthClearValue: 0f),
                Label: "geometry")))
            {
                pass.SetViewport(0, 0, Size, Size);
                pass.SetScissor(0, 0, Size, Size);

                Assert.True(meshes.Draw(
                    pass,
                    pipelines.ForState(DepthState.TestAndWrite, CullMode.None),
                    new MeshDrawCall(mesh, 0, 3, TextureHandle.None, TextureHandle.None, TextureHandle.None),
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(push),
                    frameCounter: 1,
                    surfaceGroup));
            }

            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(targets[SceneTargetId.BaseColour]),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(5)), "The base colour never came back.");

        return data.GetData().ToArray();
    }

    /// <summary>White for every slot of the material, which is the identity of every multiply.</summary>
    private sealed class WhiteTextures : ITextureBindingSource, IDisposable
    {
        private readonly IRhiTexture _white;
        private readonly IRhiSampler _sampler;

        public WhiteTextures(IRhiDevice device)
        {
            _white = device.CreateTexture(new RhiTextureDescriptor(
                1,
                1,
                RhiTextureFormat.Rgba8Unorm,
                RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
                Label: "white"));

            device.WriteTexture<byte>(
                new RhiTextureRegion(_white),
                [255, 255, 255, 255],
                4,
                new RhiExtent3D(1, 1));

            _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "white"));
        }

        public RhiTextureBinding Resolve(TextureHandle handle) => new(0, _white.View, _sampler);

        public void Dispose()
        {
            _sampler.Dispose();
            _white.Dispose();
        }
    }
}
