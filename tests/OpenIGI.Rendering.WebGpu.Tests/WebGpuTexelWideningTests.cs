using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// Narrow texels widened to the format a storage texture was allocated as, the way a sampler would have
/// read them from the narrow one.
/// </summary>
public sealed class WebGpuTexelWideningTests
{
    [Fact]
    public void One_byte_texels_become_the_colour_a_sampler_reads_from_them()
    {
        // The distance field's volume: one byte a texel, allocated as rgba32float.
        byte[] narrow = [0, 128, 255];

        byte[] wide = WebGpuTexelWidening.Widen(
            narrow,
            RhiTextureFormat.R8Unorm,
            RhiTextureFormat.Rgba32Float,
            new RhiExtent3D(3, 1),
            bytesPerRow: 3,
            out int widenedRow);

        float[] texels = MemoryMarshal.Cast<byte, float>(wide).ToArray();

        Assert.Equal(48, widenedRow);
        Assert.Equal([0f, 0f, 0f, 1f], texels[0..4]);
        Assert.Equal(128f / 255f, texels[4], 6);
        Assert.Equal([0f, 0f, 1f], texels[5..8]);
        Assert.Equal([1f, 0f, 0f, 1f], texels[8..12]);
    }

    [Fact]
    public void Padded_rows_and_slices_are_read_at_their_stride_and_written_tight()
    {
        // Two rows of two half-float pairs, each row padded to eight bytes past its data, over two slices.
        Half[] narrow =
        [
            (Half)1f, (Half)2f, (Half)3f, (Half)4f, default, default, default, default,
            (Half)5f, (Half)6f, (Half)7f, (Half)8f, default, default, default, default,
            (Half)9f, (Half)10f, (Half)11f, (Half)12f, default, default, default, default,
            (Half)13f, (Half)14f, (Half)15f, (Half)16f, default, default, default, default,
        ];

        byte[] wide = WebGpuTexelWidening.Widen(
            MemoryMarshal.AsBytes<Half>(narrow),
            RhiTextureFormat.Rg16Float,
            RhiTextureFormat.Rgba32Float,
            new RhiExtent3D(2, 1, 4),
            bytesPerRow: 16,
            out int widenedRow);

        float[] texels = MemoryMarshal.Cast<byte, float>(wide).ToArray();

        Assert.Equal(32, widenedRow);
        Assert.Equal(32, texels.Length);
        Assert.Equal([1f, 2f, 0f, 1f, 3f, 4f, 0f, 1f], texels[0..8]);
        Assert.Equal([13f, 14f, 0f, 1f, 15f, 16f, 0f, 1f], texels[24..32]);
    }

    [Fact]
    public void A_format_it_cannot_read_is_refused_rather_than_guessed()
    {
        Assert.False(WebGpuTexelWidening.Handles(RhiTextureFormat.Rg11B10Ufloat, RhiTextureFormat.Rgba32Float));
        Assert.Throws<NotSupportedException>(() => WebGpuTexelWidening.Widen(
            new byte[4],
            RhiTextureFormat.Rg11B10Ufloat,
            RhiTextureFormat.Rgba32Float,
            new RhiExtent3D(1, 1),
            bytesPerRow: 4,
            out _));
    }
}
