using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenIGI.Core;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// Writes the <c>"LOOP"</c> containers <see cref="TexFile"/> and <see cref="TexAtlasFile"/> read.
/// </summary>
/// <remarks>
/// <para>
/// The formats layer was read-only until replacement art needed somewhere to go. An overrides folder
/// holds the game's own files, so anything that produces one has to write the game's own containers —
/// and the only way to be confident about that is to write them beside the readers, with round-trip
/// tests that decode what was encoded using the same code the game does.
/// </para>
/// <para>
/// ALWAYS <see cref="TextureFormatCode.Bgra8"/>, whatever the original was. The retail art is mostly
/// <c>A1R5G5B5</c>: five bits a channel and one bit of alpha. Re-encoding to it would throw away most
/// of what an upscale just produced — the banding in a 2001 sky is that format, not the resolution —
/// and 32-bit costs bytes and nothing else, because every reader here decodes to <see cref="Bgra32"/>
/// anyway. It is the one format that is lossless by construction.
/// </para>
/// <para>
/// The version is chosen by the CALLER, not by this type, and that is deliberate: an override replaces
/// one specific file, and a file the game opens as a version 2 texture has to come back as one.
/// </para>
/// </remarks>
public static class TexWriter
{
    /// <summary>The widest layer a version 7 or 9 atlas can record.</summary>
    /// <remarks>
    /// Pitch is a <c>u16</c> in a version 9 layer descriptor, so at four bytes a pixel a tile cannot be
    /// wider than this. An 8x upscale of this game's widest texture is 2048, so there is room — but the
    /// writer refuses rather than truncating the field into a file that decodes as diagonal garbage.
    /// </remarks>
    public const int MaxAtlasWidth = (ushort.MaxValue / Bgra32.Size) - 1;

    /// <summary>Encodes one image as a single-picture <c>.tex</c> file.</summary>
    /// <param name="image">The image to write.</param>
    /// <param name="version">The container version, <see cref="TexFile.Version2"/> or <see cref="TexFile.Version11"/>.</param>
    /// <param name="secondaryFormat">The secondary format word, carried through from the original.</param>
    /// <returns>The complete file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="TextureFormatException">The version is not one the original loads.</exception>
    public static byte[] WriteTexture(TextureImage image, uint version, ushort secondaryFormat = 0)
    {
        ArgumentNullException.ThrowIfNull(image);

        return version switch
        {
            TexFile.Version2 => WriteVersion2(image, secondaryFormat),
            TexFile.Version11 => WriteVersion11(image, secondaryFormat),
            _ => throw new TextureFormatException(
                $"Texture version {version} is not one the original loads; it accepts only "
                    + $"{TexFile.Version2} and {TexFile.Version11}."),
        };
    }

    /// <summary>Encodes several equally-sized images as a multi-picture atlas.</summary>
    /// <param name="tiles">The layers, in the order the tile map indexes them.</param>
    /// <param name="version">The container version, <see cref="TexAtlasFile.Version7"/> or <see cref="TexAtlasFile.Version9"/>.</param>
    /// <param name="secondaryFormat">The secondary format word, carried through from the original.</param>
    /// <returns>The complete file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tiles"/> is <see langword="null"/>.</exception>
    /// <exception cref="TextureFormatException">The version is wrong, there are no layers, or a layer is too wide.</exception>
    /// <remarks>
    /// LAYER ORDER IS LOAD-BEARING, which is why this takes a list and not a set. Terrain names its
    /// ground by index into this atlas, so a reordered rewrite repaints a level — grass where the road
    /// was — and nothing downstream can detect it, because every index still resolves to something.
    /// </remarks>
    public static byte[] WriteAtlas(IReadOnlyList<TextureImage> tiles, uint version, ushort secondaryFormat = 0)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        if (version is not (TexAtlasFile.Version7 or TexAtlasFile.Version9))
        {
            throw new TextureFormatException(
                $"Texture atlas version {version} is not a multi-image container; it accepts only "
                    + $"{TexAtlasFile.Version7} and {TexAtlasFile.Version9}.");
        }

        if (tiles.Count == 0)
        {
            throw new TextureFormatException("An atlas needs at least one layer.");
        }

        int stride = version == TexAtlasFile.Version7
            ? TexAtlasFile.Version7LayerSize
            : TexAtlasFile.Version9LayerSize;

        int descriptors = TexAtlasFile.HeaderSize + (stride * tiles.Count);
        int payload = 0;

        foreach (TextureImage tile in tiles)
        {
            if (tile.Width > MaxAtlasWidth)
            {
                throw new TextureFormatException(
                    $"A layer {tile.Width} pixels wide needs a pitch of {tile.Width * Bgra32.Size} bytes, "
                        + $"which does not fit the 16-bit pitch field; the limit is {MaxAtlasWidth} pixels.");
            }

            payload += LevelZeroBytes(tile);
        }

        byte[] file = new byte[descriptors + payload];
        Span<byte> span = file;

        TexFile.Magic.WriteTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], version);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x12..], secondaryFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x20..], (uint)tiles.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x28..], (uint)tiles[0].Width);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x2C..], (uint)tiles[0].Height);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x30..], (uint)TextureFormatCode.Bgra8);

        int offset = descriptors;

        for (int i = 0; i < tiles.Count; i++)
        {
            TextureImage tile = tiles[i];
            Span<byte> layer = span.Slice(TexAtlasFile.HeaderSize + (i * stride), stride);
            int pitch = tile.Width * Bgra32.Size;

            BinaryPrimitives.WriteUInt32LittleEndian(layer, (uint)offset);

            if (version == TexAtlasFile.Version7)
            {
                // Version 7 holds pitch and size as dwords and takes its format from the shared word.
                BinaryPrimitives.WriteUInt32LittleEndian(layer[4..], (uint)pitch);
                BinaryPrimitives.WriteUInt32LittleEndian(layer[8..], (uint)tile.Width);
                BinaryPrimitives.WriteUInt32LittleEndian(layer[12..], (uint)tile.Height);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(layer[4..], (uint)TextureFormatCode.Bgra8);
                BinaryPrimitives.WriteUInt16LittleEndian(layer[8..], (ushort)pitch);
                BinaryPrimitives.WriteUInt16LittleEndian(layer[10..], (ushort)tile.Width);
                BinaryPrimitives.WriteUInt16LittleEndian(layer[12..], (ushort)tile.Height);
            }

            offset += WriteLevelZero(span[offset..], tile);
        }

        return file;
    }

    private static byte[] WriteVersion2(TextureImage image, ushort secondaryFormat)
    {
        // Version 2 has no depth field, no format parameter, and a u16 format word. It also has nowhere
        // to record a mip chain, so level zero is all there is to write.
        ValidateDimensions(image, TexFile.Version2, ushort.MaxValue / Bgra32.Size);

        byte[] file = new byte[TextureDescriptor.TexVersion2HeaderSize + LevelZeroBytes(image)];
        Span<byte> span = file;

        TexFile.Magic.WriteTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], TexFile.Version2);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], secondaryFormat);
        BinaryPrimitives.WriteUInt16LittleEndian(span[16..], (ushort)(image.Width * Bgra32.Size));
        BinaryPrimitives.WriteUInt16LittleEndian(span[18..], (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], (ushort)image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], (ushort)TextureFormatCode.Bgra8);

        _ = WriteLevelZero(span[TextureDescriptor.TexVersion2HeaderSize..], image);
        return file;
    }

    private static byte[] WriteVersion11(TextureImage image, ushort secondaryFormat)
    {
        // The chain is written when the image carries one, because the original's own textures do and
        // the renderer would otherwise rebuild it per texture at load. The flag and the payload have to
        // agree in both directions: MipLevels is derived from the flag, so a chain written without it is
        // read as one level, and a flag set without a chain reads past the end of the file.
        ValidateDimensions(image, TexFile.Version11, ushort.MaxValue);

        // The retail header counts a chain only while BOTH axes can be halved. An image object may carry
        // further levels for GPU sampling after its narrow axis reaches one, but version 11 has no way
        // to describe those levels; writing them would append unreachable bytes to an otherwise valid
        // texture.
        int levels = Math.Min(image.MipLevels, HeaderMipLevels(image.Width, image.Height));
        bool mipped = levels > 1;
        int payload = 0;

        for (int level = 0; level < levels; level++)
        {
            (int width, int height) = image.GetLevelSize(level);
            payload += width * height * Bgra32.Size;
        }

        byte[] file = new byte[TextureDescriptor.TexVersion11HeaderSize + payload];
        Span<byte> span = file;

        uint format = (uint)TextureFormatCode.Bgra8 | (mipped ? TextureDescriptor.MipChainFlag : 0);

        TexFile.Magic.WriteTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], TexFile.Version11);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], format);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], secondaryFormat);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(span[24..], (ushort)image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(span[30..], (ushort)Bgra32.Size);

        int offset = TextureDescriptor.TexVersion11HeaderSize;

        for (int level = 0; level < levels; level++)
        {
            ReadOnlySpan<byte> pixels = MemoryMarshal.AsBytes(image.GetLevel(level));
            pixels.CopyTo(span[offset..]);
            offset += pixels.Length;
        }

        return file;
    }

    private static void ValidateDimensions(TextureImage image, uint version, int maximumWidth)
    {
        if (image.Width > maximumWidth || image.Height > ushort.MaxValue)
        {
            throw new TextureFormatException(
                $"A {image.Width}x{image.Height} texture does not fit version {version}'s 16-bit header fields.");
        }
    }

    private static int HeaderMipLevels(int width, int height)
    {
        int levels = 1;
        while ((width >> 1) > 0 && (height >> 1) > 0)
        {
            width >>= 1;
            height >>= 1;
            levels++;
        }

        return levels;
    }

    private static int LevelZeroBytes(TextureImage image) => image.Width * image.Height * Bgra32.Size;

    private static int WriteLevelZero(Span<byte> destination, TextureImage image)
    {
        // Pitch is the visible width here, never an allocated one. The original pads a texture's stride
        // out to the width it asked video memory for; nothing written here has been through that
        // allocator, so the two are the same number, and saying so keeps the payload tight.
        ReadOnlySpan<byte> pixels = MemoryMarshal.AsBytes(image.GetLevel(0));
        pixels.CopyTo(destination);
        return pixels.Length;
    }
}
