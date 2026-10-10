using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The game's compute seam on real devices: one of the surface cache's own modules, bound the way the
/// game binds it, dispatched and read back through the seam on every backend.
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

    [AgreementTheory]
    [MemberData(nameof(RhiBackends.ComparedWithReference), MemberType = typeof(RhiBackends))]
    public void Every_backend_runs_the_screen_filter_through_the_seam_as_the_reference_does(string backend)
    {
        Half[] vulkan = FilterOn(RhiBackends.Reference);
        Half[] other = FilterOn(backend);

        output.WriteLine($"vulkan {vulkan[0]} {vulkan[1]} {vulkan[2]}, {backend} {other[0]} {other[1]} {other[2]}");

        for (int i = 0; i < vulkan.Length; i++)
        {
            Assert.Equal((float)vulkan[i], (float)other[i], 3);
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

    private static Half[] FilterOn(string backend)
    {
        using RhiBackendHost host = RhiBackends.Open(backend, Size);
        using GraphicsDevice device = new(host.Device, new DisplayMode(Size, Size, 32, 60));
        return Filter(device);
    }
}
