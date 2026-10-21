using System.Buffers.Binary;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// Decodes a texture payload into <see cref="Bgra32"/> pixels, one mip level at a time.
/// </summary>
/// <remarks>
/// <para>
/// This is a transcription of the original's <c>DownloadTexture</c> (<c>0x50AD90</c>) and the four
/// converters it dispatches to — <c>0x517FD0</c> (paletted), <c>0x517C90</c> (<c>A1R5G5B5</c>),
/// <c>0x517F00</c> (32-bit copy) and <c>0x517DD0</c> (<c>R5G6B5</c>).
/// </para>
/// <para>
/// The channel order is the point of this class. Every converter writes blue to byte 0 and red to
/// byte 2, and the 32-bit path performs no swizzle at all — so a 32-bit texture is stored on disk in
/// exactly <see cref="Bgra32"/>'s layout. Decoding through one type here means no backend has to
/// guess.
/// </para>
/// </remarks>
public static class TextureDecoder
{
    /// <summary>Decodes a texture payload.</summary>
    /// <param name="header">The texture header describing the payload.</param>
    /// <param name="payload">The stored pixels: mip level zero first, each level tightly packed at its own stride.</param>
    /// <param name="palette">The palette for <see cref="TextureFormatCode.Palette8"/> textures; ignored otherwise.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="TextureFormatException">The format is unsupported, the payload is short, or a paletted texture has no palette.</exception>
    public static TextureImage Decode(
        in TextureDescriptor header,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<Bgra32> palette = default)
    {
        int mipLevels = header.MipLevels;

        if (header.Format == TextureFormatCode.Palette8 && palette.IsEmpty)
        {
            throw new TextureFormatException("A paletted texture was decoded without a palette.");
        }

        // Total decoded size: every level's visible pixels, back to back.
        int[] levelOffsets = new int[mipLevels];
        int total = 0;
        for (int level = 0; level < mipLevels; level++)
        {
            levelOffsets[level] = total;
            total += (header.Width >> level) * (header.Height >> level);
        }

        Bgra32[] pixels = new Bgra32[total];

        int sourceOffset = 0;
        int pitch = header.Pitch;
        for (int level = 0; level < mipLevels; level++)
        {
            int width = header.Width >> level;
            int height = header.Height >> level;
            int levelBytes = pitch * height;

            if (levelBytes > payload.Length - sourceOffset)
            {
                throw new TextureFormatException(
                    $"Texture payload is {payload.Length} bytes; mip level {level} needs {levelBytes} more at offset {sourceOffset}.");
            }

            DecodeLevel(
                header.Format,
                payload.Slice(sourceOffset, levelBytes),
                pitch,
                width,
                height,
                palette,
                pixels.AsSpan(levelOffsets[level], width * height));

            sourceOffset += levelBytes;

            // The original halves the stride alongside the dimensions rather than recomputing it,
            // which is the same thing for a power-of-two allocated width and is what odd sizes follow.
            pitch >>= 1;
        }

        return new TextureImage(header.Width, header.Height, mipLevels, pixels, levelOffsets);
    }

    private static void DecodeLevel(
        TextureFormatCode format,
        ReadOnlySpan<byte> source,
        int pitch,
        int width,
        int height,
        ReadOnlySpan<Bgra32> palette,
        Span<Bgra32> destination)
    {
        switch (format)
        {
            case TextureFormatCode.Palette8:
                DecodePalette8(source, pitch, width, height, palette, destination);
                break;
            case TextureFormatCode.A1R5G5B5:
                DecodeA1R5G5B5(source, pitch, width, height, destination);
                break;
            case TextureFormatCode.Bgra8:
            case TextureFormatCode.Bgra8Alternate:
                DecodeBgra8(source, pitch, width, height, destination);
                break;
            case TextureFormatCode.R5G6B5:
                DecodeR5G6B5(source, pitch, width, height, destination);
                break;
            default:
                throw new TextureFormatException(
                    $"Unsupported texture format {(int)format}. The original rejects these with \"Unsupported texture format\".");
        }
    }

    /// <summary>Eight-bit palette indices. Mirrors <c>0x517FD0</c>.</summary>
    private static void DecodePalette8(
        ReadOnlySpan<byte> source,
        int pitch,
        int width,
        int height,
        ReadOnlySpan<Bgra32> palette,
        Span<Bgra32> destination)
    {
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = source.Slice(y * pitch, width);
            Span<Bgra32> target = destination.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                byte index = row[x];
                target[x] = index < palette.Length ? palette[index] : Bgra32.Transparent;
            }
        }
    }

    /// <summary>Sixteen-bit <c>A1R5G5B5</c>. Mirrors <c>0x517C90</c>.</summary>
    private static void DecodeA1R5G5B5(
        ReadOnlySpan<byte> source,
        int pitch,
        int width,
        int height,
        Span<Bgra32> destination)
    {
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = source.Slice(y * pitch, width * sizeof(ushort));
            Span<Bgra32> target = destination.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                ushort s = BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]);
                target[x] = new Bgra32(
                    b: (byte)((s & 0x1F) << 3),
                    g: (byte)((s >> 2) & 0xF8),
                    r: (byte)((s >> 7) & 0xF8),
                    a: (s & 0x8000) != 0 ? (byte)0xFF : (byte)0x00);
            }
        }
    }

    /// <summary>Sixteen-bit <c>R5G6B5</c>, always opaque. Mirrors <c>0x517DD0</c>.</summary>
    private static void DecodeR5G6B5(
        ReadOnlySpan<byte> source,
        int pitch,
        int width,
        int height,
        Span<Bgra32> destination)
    {
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = source.Slice(y * pitch, width * sizeof(ushort));
            Span<Bgra32> target = destination.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                ushort s = BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]);
                target[x] = new Bgra32(
                    b: (byte)((s & 0x1F) << 3),
                    g: (byte)((s >> 3) & 0xFC),
                    r: (byte)((s >> 8) & 0xF8),
                    a: 0xFF);
            }
        }
    }

    /// <summary>Thirty-two-bit, copied unchanged. Mirrors <c>0x517F00</c>.</summary>
    private static void DecodeBgra8(
        ReadOnlySpan<byte> source,
        int pitch,
        int width,
        int height,
        Span<Bgra32> destination)
    {
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = source.Slice(y * pitch, width * Bgra32.Size);
            Span<Bgra32> target = destination.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                target[x] = Bgra32.FromPacked(BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]));
            }
        }
    }
}
