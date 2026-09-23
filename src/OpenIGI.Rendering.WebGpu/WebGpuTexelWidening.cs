using System.Buffers.Binary;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// Texels written in the format a texture was asked for, turned into the format it was allocated as.
/// </summary>
/// <remarks>
/// <para>
/// A storage texture in a format WGSL has no storage form for is allocated in the wider one its modules
/// were compiled against (see <see cref="WebGpuFormatPolicy"/>). The caller still asked for the narrow
/// one and still hands over narrow texels: a distance field's volume is one byte a texel whatever the
/// backend. The widening is this backend's own business, so the texels are widened here rather than the
/// caller being told to know about it.
/// </para>
/// <para>
/// Each texel is read as the colour a sampler would return for it — missing channels nought, a missing
/// alpha one — and written in the wider format. That is exactly what a shader reading the narrow format
/// would have seen, which is the point: the module reads the wide texture as it would the narrow one.
/// </para>
/// </remarks>
public static class WebGpuTexelWidening
{
    /// <summary>Whether texels of one format can be widened to another here.</summary>
    /// <param name="from">The format they are in.</param>
    /// <param name="to">The format wanted.</param>
    /// <returns><see langword="true"/> when <see cref="Widen"/> takes the pair.</returns>
    public static bool Handles(RhiTextureFormat from, RhiTextureFormat to) =>
        Channels(from) > 0 && Channels(to) > 0;

    /// <summary>Widens a block of texels.</summary>
    /// <param name="data">The texels, rows <paramref name="bytesPerRow"/> apart.</param>
    /// <param name="from">The format they are in.</param>
    /// <param name="to">The format to write them in.</param>
    /// <param name="size">How many texels along each axis.</param>
    /// <param name="bytesPerRow">How far apart the rows of <paramref name="data"/> are.</param>
    /// <param name="widenedBytesPerRow">How far apart the rows of the result are: tightly packed.</param>
    /// <returns>The texels in <paramref name="to"/>.</returns>
    /// <exception cref="NotSupportedException">One of the formats is not one this knows how to read or write.</exception>
    public static byte[] Widen(
        ReadOnlySpan<byte> data,
        RhiTextureFormat from,
        RhiTextureFormat to,
        in RhiExtent3D size,
        int bytesPerRow,
        out int widenedBytesPerRow)
    {
        if (!Handles(from, to))
        {
            throw new NotSupportedException($"Texels cannot be widened from {from} to {to} here.");
        }

        int inStride = from.BytesPerTexel();
        int outStride = to.BytesPerTexel();
        int rows = size.Height * size.DepthOrArrayLayers;

        widenedBytesPerRow = size.Width * outStride;
        byte[] widened = new byte[widenedBytesPerRow * rows];
        Span<float> texel = stackalloc float[4];

        for (int row = 0; row < rows; row++)
        {
            ReadOnlySpan<byte> source = data.Slice(row * bytesPerRow, size.Width * inStride);
            Span<byte> target = widened.AsSpan(row * widenedBytesPerRow, widenedBytesPerRow);

            for (int x = 0; x < size.Width; x++)
            {
                Read(source.Slice(x * inStride, inStride), from, texel);
                Write(texel, to, target.Slice(x * outStride, outStride));
            }
        }

        return widened;
    }

    /// <summary>How many channels a format has, or nought for one this does not read or write.</summary>
    private static int Channels(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm or RhiTextureFormat.R16Float or RhiTextureFormat.R32Float => 1,
        RhiTextureFormat.Rg8Unorm or RhiTextureFormat.Rg16Float or RhiTextureFormat.Rg32Float => 2,
        RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba16Float or RhiTextureFormat.Rgba32Float => 4,
        _ => 0,
    };

    private static void Read(ReadOnlySpan<byte> source, RhiTextureFormat format, Span<float> texel)
    {
        texel[0] = 0f;
        texel[1] = 0f;
        texel[2] = 0f;
        texel[3] = 1f;

        int channels = Channels(format);
        int width = format.BytesPerTexel() / channels;

        for (int channel = 0; channel < channels; channel++)
        {
            ReadOnlySpan<byte> value = source.Slice(channel * width, width);
            texel[channel] = width switch
            {
                1 => value[0] / 255f,
                2 => (float)BinaryPrimitives.ReadHalfLittleEndian(value),
                _ => BinaryPrimitives.ReadSingleLittleEndian(value),
            };
        }
    }

    private static void Write(ReadOnlySpan<float> texel, RhiTextureFormat format, Span<byte> target)
    {
        int channels = Channels(format);
        int width = format.BytesPerTexel() / channels;

        for (int channel = 0; channel < channels; channel++)
        {
            Span<byte> value = target.Slice(channel * width, width);
            switch (width)
            {
                case 1:
                    value[0] = (byte)Math.Clamp(MathF.Round(texel[channel] * 255f), 0f, 255f);
                    break;

                case 2:
                    BinaryPrimitives.WriteHalfLittleEndian(value, (Half)texel[channel]);
                    break;

                default:
                    BinaryPrimitives.WriteSingleLittleEndian(value, texel[channel]);
                    break;
            }
        }
    }
}
