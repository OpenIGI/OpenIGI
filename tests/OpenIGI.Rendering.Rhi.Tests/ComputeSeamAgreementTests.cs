using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The game's compute seam on real devices: one of the surface cache's own modules, bound the way the
/// game binds it, dispatched and read back through the seam on both backends.
/// </summary>
/// <remarks>
/// The screen filter smooths the accumulated gather across neighbours that share a surface. Fed a field
/// that is the same everywhere — one colour, one position, one normal — every neighbour agrees with every
/// other, so whatever the weights are, what comes out is what went in. That makes the answer known
/// without re-deriving the filter, and makes a wrong binding, format or unit visible as a wrong colour.
/// </remarks>
public sealed class ComputeSeamAgreementTests(ITestOutputHelper output)
{
    private const int Size = 16;

    [Fact]
    public void Both_backends_run_the_screen_filter_through_the_seam_alike()
    {
        Half[]? vulkan = TryOn(out string? vulkanReason, webgpu: false);
        Half[]? webgpu = TryOn(out string? webgpuReason, webgpu: true);

        Assert.True(vulkan is not null && webgpu is not null, vulkanReason ?? webgpuReason);

        output.WriteLine($"vulkan {vulkan![0]} {vulkan[1]} {vulkan[2]}, webgpu {webgpu![0]} {webgpu[1]} {webgpu[2]}");

        for (int i = 0; i < vulkan.Length; i++)
        {
            Assert.Equal((float)vulkan[i], (float)webgpu[i], 3);
        }

        // Every texel of the result is the colour that went in.
        for (int texel = 0; texel < Size * Size; texel++)
        {
            Assert.Equal(0.75f, (float)vulkan[(texel * 4) + 0], 2);
            Assert.Equal(0.5f, (float)vulkan[(texel * 4) + 1], 2);
            Assert.Equal(0.25f, (float)vulkan[(texel * 4) + 2], 2);
        }
    }

    private static Half[] Filter(GraphicsDevice device)
    {
        TextureHandle filtered = device.CreateStorageTexture(Size, Size, VolumeFormat.Rgba16F, "filtered");
        TextureHandle accumulated = Uniform(device, "accumulated", 0.75f, 0.5f, 0.25f, 1f);
        TextureHandle positions = Uniform(device, "positions", 1f, 2f, 3f, 1f);
        TextureHandle normals = Uniform(device, "normals", 0f, 0f, 1f, 1f);

        ComputeShaderHandle filter = device.CreateComputeShader(
            ShaderModuleId.CardsScreenFilter,
            ShaderVariant.Default,
            "cards.screen.filter");

        // As SurfaceCacheLighting binds it: the image, three textures on units, and the units by name.
        device.BindImage(0, filtered, ImageAccess.Write);
        device.SetTexture(1, accumulated);
        device.SetTexture(2, positions);
        device.SetTexture(3, normals);
        device.SetComputeUniform(filter, "uAccum", 1);
        device.SetComputeUniform(filter, "uPosition", 2);
        device.SetComputeUniform(filter, "uSceneNormal", 3);
        device.SetComputeUniform(filter, "uWidth", Size);
        device.SetComputeUniform(filter, "uHeight", Size);
        device.SetComputeUniform(filter, "uRadius", 2);
        device.Dispatch(filter, Size / 8, Size / 8);

        Half[] result = new Half[Size * Size * 4];
        device.ReadVolumeTexture<Half>(filtered, result);
        return result;
    }

    private static TextureHandle Uniform(GraphicsDevice device, string name, float r, float g, float b, float a)
    {
        TextureHandle texture = device.CreateStorageTexture(Size, Size, VolumeFormat.Rgba16F, name);
        Half[] texels = new Half[Size * Size * 4];

        for (int i = 0; i < Size * Size; i++)
        {
            texels[(i * 4) + 0] = (Half)r;
            texels[(i * 4) + 1] = (Half)g;
            texels[(i * 4) + 2] = (Half)b;
            texels[(i * 4) + 3] = (Half)a;
        }

        device.UpdateVolumeTexture<Half>(texture, texels);
        return texture;
    }

    private static Half[]? TryOn(out string? reason, bool webgpu)
    {
        reason = null;

        try
        {
            if (webgpu)
            {
                using WebGpuRhiDevice rhi = WebGpuRhiDevice.Create();
                rhi.AttachOffscreen(Size, Size, GraphicsDevice.DefaultPresentFormat);
                using GraphicsDevice device = new(rhi, new DisplayMode(Size, Size, 32, 60));
                return Filter(device);
            }

            using VulkanRhi vulkan = VulkanRhi.CreateOffscreen(Size, Size, GraphicsDevice.DefaultPresentFormat);
            using GraphicsDevice onVulkan = new(vulkan.Device, new DisplayMode(Size, Size, 32, 60));
            return Filter(onVulkan);
        }
        catch (Exception error) when (error is not Xunit.Sdk.XunitException)
        {
            reason = $"{(webgpu ? "WebGPU" : "Vulkan")}: {error.Message}";
            return null;
        }
    }
}
