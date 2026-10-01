using System.Buffers.Binary;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// A Truevision TGA image, as I.G.I. 2 ships its sky, water and effect textures.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here reproduces game behaviour, and that is a finding rather than an omission.</b>
/// I.G.I. 2 does not decode these itself: <c>Direct3DTexture_DownloadFromFile</c>
/// (<c>igi2ed 0x662FE0</c>) hands the raw file bytes to <c>D3DXCreateTextureFromFileInMemoryEx</c>,
/// which sniffs the format and decodes it — the binary even carries a statically linked libjpeg inside
/// D3DX for the other half. So there is no engine quirk to match: a standard-conformant reader is
/// exactly right, and the format's own conventions are the specification.
/// </para>
/// <para>
/// A level names these files directly, with their own extension and alias —
/// <c>MISSION:textures/clouds1.tga</c> — and they are loose files beside the level's archive rather
/// than entries inside it. 84 such references appear across the shipped level scripts.
/// </para>
/// <para>
/// The retail corpus uses four shapes, none of them run-length encoded: 162 uncompressed 24-bit,
/// 31 uncompressed 32-bit, 3 colour-mapped 8-bit and 1 greyscale. The run-length forms are decoded
/// anyway, because they share the reader's tail and refusing a format the file says it is would be a
/// worse failure than handling it.
/// </para>
/// </remarks>
public static class TgaImage
{
    /// <summary>The size in bytes of the header that begins every TGA.</summary>
    public const int HeaderSize = 18;

    /// <summary>Reads a TGA into top-down <see cref="Bgra32"/> pixels.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The image, with one mip level.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a TGA this reader handles.</exception>
    /// <remarks>
    /// The result is always top-down. A TGA stores its origin in the descriptor's bit 5, and the retail
    /// corpus leaves it clear throughout, which means bottom-up rows that have to be flipped; a file
    /// that sets it is taken as already top-down.
    /// </remarks>
    public static TextureImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new TextureFormatException($"A TGA needs at least {HeaderSize} bytes, got {data.Length}.");
        }

        int idLength = data[0];
        int colourMapType = data[1];
        int imageType = data[2];
        int colourMapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
        int colourMapDepth = data[7];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        int depth = data[16];
        int descriptor = data[17];

        if (width <= 0 || height <= 0)
        {
            throw new TextureFormatException($"A TGA is {width}x{height}, which has no pixels.");
        }

        bool runLength = imageType is 9 or 10 or 11;
        int kind = runLength ? imageType - 8 : imageType;
        if (kind is not (1 or 2 or 3))
        {
            throw new TextureFormatException($"A TGA of image type {imageType} is not one this reader handles.");
        }

        int offset = HeaderSize + idLength;
        ReadOnlySpan<Bgra32> palette = default;
        if (colourMapType == 1)
        {
            int entrySize = (colourMapDepth + 7) / 8;
            int bytes = colourMapLength * entrySize;
            if (offset + bytes > data.Length)
            {
                throw new TextureFormatException(
                    $"A TGA declares a {colourMapLength}-entry colour map that does not fit its {data.Length} bytes.");
            }

            palette = ReadPalette(data.Slice(offset, bytes), colourMapLength, entrySize);
            offset += bytes;
        }
        else if (kind == 1)
        {
            throw new TextureFormatException("A colour-mapped TGA carries no colour map.");
        }

        int bytesPerPixel = (depth + 7) / 8;
        if (kind == 1 ? depth != 8 : bytesPerPixel is not (1 or 2 or 3 or 4))
        {
            throw new TextureFormatException($"A TGA of {depth} bits per pixel is not one this reader handles.");
        }

        Bgra32[] pixels = new Bgra32[width * height];
        ReadOnlySpan<byte> body = data[offset..];
        if (runLength)
        {
            DecodeRunLength(body, pixels, bytesPerPixel, kind, palette);
        }
        else
        {
            DecodeRaw(body, pixels, bytesPerPixel, kind, palette);
        }

        // Bit 5 of the descriptor is the vertical origin: clear means the first row stored is the
        // bottom one, which is what every retail file says.
        if ((descriptor & 0x20) == 0)
        {
            FlipVertically(pixels, width, height);
        }

        return new TextureImage(width, height, 1, pixels, [0]);
    }

    /// <summary>Gets a value indicating whether the bytes begin like a TGA this reader handles.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns><see langword="true"/> when the header is plausible.</returns>
    /// <remarks>
    /// TGA has no magic number — the format predates the convention — so this is a shape check rather
    /// than an identification, and it is only sound once the caller has ruled out the formats that do
    /// have one.
    /// </remarks>
    public static bool IsTga(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            return false;
        }

        int colourMapType = data[1];
        int imageType = data[2];
        int depth = data[16];

        return colourMapType is 0 or 1
            && imageType is 1 or 2 or 3 or 9 or 10 or 11
            && depth is 8 or 15 or 16 or 24 or 32
            && BinaryPrimitives.ReadUInt16LittleEndian(data[12..]) > 0
            && BinaryPrimitives.ReadUInt16LittleEndian(data[14..]) > 0;
    }

    private static Bgra32[] ReadPalette(ReadOnlySpan<byte> data, int count, int entrySize)
    {
        Bgra32[] palette = new Bgra32[count];
        for (int i = 0; i < count; i++)
        {
            palette[i] = ToPixel(data.Slice(i * entrySize, entrySize), entrySize, kind: 2, default);
        }

        return palette;
    }

    private static void DecodeRaw(
        ReadOnlySpan<byte> body,
        Span<Bgra32> pixels,
        int bytesPerPixel,
        int kind,
        ReadOnlySpan<Bgra32> palette)
    {
        long needed = (long)pixels.Length * bytesPerPixel;
        if (needed > body.Length)
        {
            throw new TextureFormatException(
                $"A TGA needs {needed} bytes of pixels but only {body.Length} remain.");
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = ToPixel(body.Slice(i * bytesPerPixel, bytesPerPixel), bytesPerPixel, kind, palette);
        }
    }

    private static void DecodeRunLength(
        ReadOnlySpan<byte> body,
        Span<Bgra32> pixels,
        int bytesPerPixel,
        int kind,
        ReadOnlySpan<Bgra32> palette)
    {
        int written = 0;
        int offset = 0;
        while (written < pixels.Length)
        {
            if (offset >= body.Length)
            {
                throw new TextureFormatException(
                    $"A run-length TGA ran out after {written} of {pixels.Length} pixels.");
            }

            int packet = body[offset++];
            int count = (packet & 0x7F) + 1;
            if (written + count > pixels.Length)
            {
                throw new TextureFormatException("A run-length TGA declares a packet running past its last pixel.");
            }

            if ((packet & 0x80) != 0)
            {
                // A run: one pixel repeated.
                if (offset + bytesPerPixel > body.Length)
                {
                    throw new TextureFormatException("A run-length TGA ended inside a run packet.");
                }

                Bgra32 value = ToPixel(body.Slice(offset, bytesPerPixel), bytesPerPixel, kind, palette);
                offset += bytesPerPixel;
                pixels.Slice(written, count).Fill(value);
            }
            else
            {
                // A literal: `count` pixels, stored one after another.
                if (offset + (count * bytesPerPixel) > body.Length)
                {
                    throw new TextureFormatException("A run-length TGA ended inside a literal packet.");
                }

                for (int i = 0; i < count; i++)
                {
                    pixels[written + i] = ToPixel(
                        body.Slice(offset + (i * bytesPerPixel), bytesPerPixel),
                        bytesPerPixel,
                        kind,
                        palette);
                }

                offset += count * bytesPerPixel;
            }

            written += count;
        }
    }

    private static Bgra32 ToPixel(ReadOnlySpan<byte> source, int bytesPerPixel, int kind, ReadOnlySpan<Bgra32> palette)
    {
        if (kind == 1)
        {
            int index = source[0];
            return index < palette.Length ? palette[index] : default;
        }

        if (kind == 3)
        {
            // Greyscale: one channel across all three, fully opaque.
            byte grey = source[0];
            return new Bgra32(grey, grey, grey, 255);
        }

        return bytesPerPixel switch
        {
            // 16-bit is A1R5G5B5 stored little-endian, which is the only packed form TGA defines.
            2 => FromPacked(BinaryPrimitives.ReadUInt16LittleEndian(source)),
            3 => new Bgra32(source[0], source[1], source[2], 255),
            4 => new Bgra32(source[0], source[1], source[2], source[3]),
            _ => new Bgra32(source[0], source[0], source[0], 255),
        };
    }

    private static Bgra32 FromPacked(ushort value) =>
        new(
            (byte)(((value & 0x1F) * 255) / 31),
            (byte)((((value >> 5) & 0x1F) * 255) / 31),
            (byte)((((value >> 10) & 0x1F) * 255) / 31),
            (byte)((value & 0x8000) != 0 ? 255 : 0));

    private static void FlipVertically(Span<Bgra32> pixels, int width, int height)
    {
        Bgra32[] row = new Bgra32[width];

        for (int y = 0; y < height / 2; y++)
        {
            Span<Bgra32> top = pixels.Slice(y * width, width);
            Span<Bgra32> bottom = pixels.Slice((height - 1 - y) * width, width);
            top.CopyTo(row);
            bottom.CopyTo(top);
            row.CopyTo(bottom);
        }
    }
}
