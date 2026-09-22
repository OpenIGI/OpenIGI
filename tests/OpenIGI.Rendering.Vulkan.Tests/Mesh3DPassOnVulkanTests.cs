using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Rhi;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The world path drawing for real, and landing where it should.
/// </summary>
/// <remarks>
/// <para>
/// The one thing about a ported 3D path that cannot be checked by looking at recorded commands: which
/// way up it comes out. Every mesh vertex stage turns one of the engine's OpenGL projections into clip
/// space, and which way that clip space runs vertically is not the same on the two backends. The RHI
/// states WebGPU's convention and its Vulkan implementation reaches it with a flipped viewport, so a
/// stage that also flipped would undo it and the world would be upside down — silently, and only on
/// this path.
/// </para>
/// <para>
/// A triangle in the upper half of the screen, drawn through the real forward pipeline, read back.
/// </para>
/// </remarks>
public sealed class Mesh3DPassOnVulkanTests(ITestOutputHelper output)
{
    private const int Size = 64;

    [VulkanFact]
    public void A_triangle_in_the_upper_half_is_drawn_in_the_upper_half()
    {
        HeadlessVulkan? vulkan = HeadlessVulkan.TryCreate(out string? reason);
        Assert.True(vulkan is not null, reason);

        using (vulkan)
        {
            byte[] pixels = DrawTriangle(vulkan!.Rhi);

            output.WriteLine($"upper: {Alpha(pixels, Size / 2, Size / 4)}");
            output.WriteLine($"lower: {Alpha(pixels, Size / 2, Size * 3 / 4)}");

            Assert.True(Alpha(pixels, Size / 2, Size / 4) > 0, "Nothing was drawn in the upper half.");
            Assert.Equal(0, Alpha(pixels, Size / 2, Size * 3 / 4));
        }
    }

    /// <summary>The alpha of one pixel, which is what says whether the triangle covered it.</summary>
    private static int Alpha(byte[] pixels, int x, int y)
    {
        int row = RhiBufferTextureCopy.AlignRow(Size * 4);
        return pixels[(y * row) + (x * 4) + 3];
    }

    private static byte[] DrawTriangle(IRhiDevice device)
    {
        using WhiteTextures textures = new(device);
        using IRhiBindGroupLayout material = Mesh3DPipelineCache.CreateMaterialLayout(device);
        using IRhiBindGroupLayout cube = Mesh3DPipelineCache.CreateCubeLayout(device);
        using MaterialBindGroupCache materials = new(device, material);

        using Mesh3DPipelineCache pipelines = new(
            device,
            material,
            cube,
            RhiTextureFormat.Rgba8Unorm,
            RhiTextureFormat.Depth32Float);

        Mesh3DPass meshes = new(textures, materials);

        // Authored straight in clip space, so an identity projection puts them where they say: the upper
        // half is positive Y under the convention the RHI states.
        using RhiMesh mesh = RhiMesh.CreateStatic(
            device,
            [
                new Vertex3D(new Vector3f(-0.8f, 0.2f, 0f), Vector3f.UnitZ, 0f, 0f),
                new Vertex3D(new Vector3f(0.8f, 0.2f, 0f), Vector3f.UnitZ, 1f, 0f),
                new Vertex3D(new Vector3f(0f, 0.9f, 0f), Vector3f.UnitZ, 0.5f, 1f),
            ],
            [0, 1, 2],
            "triangle");

        using IRhiTexture target = device.CreateTexture(new RhiTextureDescriptor(
            Size,
            Size,
            RhiTextureFormat.Rgba8Unorm,
            RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource,
            Label: "mesh3d.target"));

        using IRhiTexture depth = device.CreateTexture(new RhiTextureDescriptor(
            Size,
            Size,
            RhiTextureFormat.Depth32Float,
            RhiTextureUsage.RenderTarget,
            Label: "mesh3d.depth"));

        int row = RhiBufferTextureCopy.AlignRow(Size * 4);

        using IRhiBuffer readback = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(row * Size),
            RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
            "mesh3d.readback"));

        float[] push = new float[ForwardPushBlock.Floats];
        ForwardPushBlock.Write(push, State, Matrix4f.Identity, BlendMode.Opaque, hasLightmap: true);

        using (IRhiCommandEncoder encoder = device.CreateCommandEncoder("mesh3d"))
        {
            using (IRhiRenderPassEncoder pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor(
                [new RhiColorAttachment(target.View, RhiLoadOp.Clear, RhiStoreOp.Store)],
                new RhiDepthAttachment(depth.View, DepthClearValue: 0f),
                Label: "mesh3d")))
            {
                pass.SetViewport(0, 0, Size, Size);
                pass.SetScissor(0, 0, Size, Size);

                Assert.True(meshes.Draw(
                    pass,
                    pipelines.ForState(DepthState.TestAndWrite, CullMode.None, BlendMode.Opaque),
                    new MeshDrawCall(mesh, 0, 3, TextureHandle.None, TextureHandle.None, TextureHandle.None),
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(push),
                    frameCounter: 1));
            }

            encoder.CopyTextureToBuffer(
                new RhiTextureRegion(target),
                new RhiBufferTextureCopy(readback, 0, row, Size),
                new RhiExtent3D(Size, Size));

            device.Submit([encoder.Finish()]);
        }

        using IRhiReadback data = device.ReadBuffer(readback);
        Assert.True(data.Wait(TimeSpan.FromSeconds(5)), "The target never came back.");

        return data.GetData().ToArray();
    }

    /// <summary>A world draw in a scene with no fog, no sun and nothing to key a rig with.</summary>
    private static ForwardDrawState State => new(
        Matrix4f.Identity,
        SceneLighting.Baked,
        SceneFog.None,
        FogTarget.Scene,
        DetailFade.None,
        Vector3f.One,
        Vector3f.Zero,
        LightmapScale: 1f,
        PoseBlend: 0f,
        Exposure: 1f,
        LampLight: Vector3f.Zero,
        FirstPerson: false);

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
