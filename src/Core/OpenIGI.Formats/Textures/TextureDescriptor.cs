using System.Buffers.Binary;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// Everything needed to decode a texture payload, normalised from the three different on-disk headers
/// the original reads.
/// </summary>
/// <remarks>
/// <para>
/// The original has three texture headers and they do <em>not</em> agree, which is why this type
/// exists as a normalisation step rather than as a struct overlay:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A <c>TEXH</c> chunk (fonts, sprites) stores <b>bits</b> per pixel and derives the stride as
/// <c>allocatedWidth * bitsPerPixel / 8</c> — <c>Texture_LoadFromIlff</c>, <c>0x4B2070</c>.
/// </description></item>
/// <item><description>
/// A <c>.tex</c> version 11 file has the same field order but stores <b>bytes</b> per pixel in the
/// same slot and derives the stride as <c>allocatedWidth * bytesPerPixel</c> —
/// <c>Texture_LoadFile</c>, <c>0x4B24C0</c>. Reading it as a <c>TEXH</c> gives a stride eight times
/// too small.
/// </description></item>
/// <item><description>
/// A <c>.tex</c> version 2 file has a shorter, differently ordered header that stores the stride
/// directly and has no depth field at all.
/// </description></item>
/// </list>
/// </remarks>
public readonly struct TextureDescriptor
{
    /// <summary>The size of a <c>TEXH</c> chunk in bytes.</summary>
    public const int TexhSize = 24;

    /// <summary>The size of a <c>.tex</c> version 2 header, magic and version included.</summary>
    public const int TexVersion2HeaderSize = 24;

    /// <summary>The size of a <c>.tex</c> version 11 header, magic and version included.</summary>
    public const int TexVersion11HeaderSize = 32;

    /// <summary>The bit of <see cref="FormatAndFlags"/> that marks a payload as carrying a mip chain.</summary>
    public const uint MipChainFlag = 0x40;

    private TextureDescriptor(
        uint formatAndFlags,
        uint formatParameter,
        ushort secondaryFormat,
        int width,
        int height,
        int pitch)
    {
        FormatAndFlags = formatAndFlags;
        FormatParameter = formatParameter;
        SecondaryFormat = secondaryFormat;
        Width = width;
        Height = height;
        Pitch = pitch;
    }

    /// <summary>Gets the raw format word: the format code with <see cref="MipChainFlag"/> possibly set.</summary>
    public uint FormatAndFlags { get; }

    /// <summary>Gets the format parameter, which selects the alpha depth the original asks of video memory.</summary>
    /// <remarks>Does not affect decoding — only backend storage.</remarks>
    public uint FormatParameter { get; }

    /// <summary>Gets the secondary format code, passed through to the backend's allocator.</summary>
    public ushort SecondaryFormat { get; }

    /// <summary>Gets the visible width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the visible height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the stride of mip level zero in bytes.</summary>
    public int Pitch { get; }

    /// <summary>Gets the pixel format, with the mip flag masked off.</summary>
    public TextureFormatCode Format => (TextureFormatCode)(FormatAndFlags & ~MipChainFlag);

    /// <summary>Gets a value indicating whether the payload holds a mip chain after level zero.</summary>
    public bool HasMipChain => (FormatAndFlags & MipChainFlag) != 0;

    /// <summary>Gets the number of mip levels the payload holds.</summary>
    /// <remarks>
    /// The original halves both axes together and stops as soon as either would reach zero, so the
    /// chain ends where the <em>smaller</em> axis reaches one: a 128×64 texture has seven levels, not
    /// eight.
    /// </remarks>
    public int MipLevels
    {
        get
        {
            if (!HasMipChain)
            {
                return 1;
            }

            int levels = 1;
            int w = Width;
            int h = Height;
            while ((w >> 1) > 0 && (h >> 1) > 0)
            {
                w >>= 1;
                h >>= 1;
                levels++;
            }

            return levels;
        }
    }

    /// <summary>Gets the total number of payload bytes the whole mip chain occupies.</summary>
    public int PayloadSize
    {
        get
        {
            int total = 0;
            int pitch = Pitch;
            for (int level = 0; level < MipLevels; level++)
            {
                total += pitch * (Height >> level);
                pitch >>= 1;
            }

            return total;
        }
    }

    /// <summary>Reads a <c>TEXH</c> chunk payload.</summary>
    /// <param name="data">The chunk payload, at least <see cref="TexhSize"/> bytes.</param>
    /// <returns>The normalised descriptor.</returns>
    /// <exception cref="TextureFormatException">The chunk is short or describes an unusable texture.</exception>
    /// <remarks>The stride is <c>allocatedWidth * bitsPerPixel / 8</c>, matching <c>0x4B2070</c>.</remarks>
    public static TextureDescriptor ReadTexh(ReadOnlySpan<byte> data)
    {
        Require(data, TexhSize, "TEXH chunk");

        ushort allocatedWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data[22..]);

        if (bitsPerPixel == 0 || bitsPerPixel % 8 != 0)
        {
            throw new TextureFormatException($"TEXH bit depth {bitsPerPixel} is not a whole number of bytes.");
        }

        return Validate(new TextureDescriptor(
            BinaryPrimitives.ReadUInt32LittleEndian(data),
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[16..]),
            allocatedWidth * bitsPerPixel / 8));
    }

    /// <summary>Reads a <c>.tex</c> version 2 file header.</summary>
    /// <param name="data">The file bytes, starting at the <c>"LOOP"</c> magic.</param>
    /// <returns>The normalised descriptor.</returns>
    /// <exception cref="TextureFormatException">The header is short or describes an unusable texture.</exception>
    /// <remarks>
    /// Field order from <c>0x4B24C0</c>: stride at +16, width at +18, height at +20, format at +22
    /// (a <em>u16</em> here, not a u32), secondary format at +8. There is no format parameter and no
    /// depth field; the payload begins at +24.
    /// </remarks>
    public static TextureDescriptor ReadTexVersion2(ReadOnlySpan<byte> data)
    {
        Require(data, TexVersion2HeaderSize, ".tex version 2 header");

        return Validate(new TextureDescriptor(
            BinaryPrimitives.ReadUInt16LittleEndian(data[22..]),
            0,
            BinaryPrimitives.ReadUInt16LittleEndian(data[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[18..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[20..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[16..])));
    }

    /// <summary>Reads a <c>.tex</c> version 11 file header.</summary>
    /// <param name="data">The file bytes, starting at the <c>"LOOP"</c> magic.</param>
    /// <returns>The normalised descriptor.</returns>
    /// <exception cref="TextureFormatException">The header is short or describes an unusable texture.</exception>
    /// <remarks>
    /// Field order from <c>0x4B24C0</c>: format at +8, format parameter at +12, secondary format at
    /// +20, width at +22, height at +24, allocated width at +26, <b>bytes</b> per pixel at +30. The
    /// payload begins at +32.
    /// </remarks>
    public static TextureDescriptor ReadTexVersion11(ReadOnlySpan<byte> data)
    {
        Require(data, TexVersion11HeaderSize, ".tex version 11 header");

        ushort allocatedWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]);
        ushort bytesPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data[30..]);

        if (bytesPerPixel == 0)
        {
            throw new TextureFormatException(".tex version 11 declares zero bytes per pixel.");
        }

        return Validate(new TextureDescriptor(
            BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[20..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[22..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[24..]),
            allocatedWidth * bytesPerPixel));
    }

    /// <summary>Creates a descriptor from explicit fields, for the sprite headers that carry them inline.</summary>
    /// <param name="format">The format word, mip flag included.</param>
    /// <param name="width">The visible width in pixels.</param>
    /// <param name="height">The visible height in pixels.</param>
    /// <param name="pitch">The stride of mip level zero in bytes.</param>
    /// <param name="secondaryFormat">The secondary format code.</param>
    /// <param name="formatParameter">The format parameter.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="TextureFormatException">The values do not describe a usable texture.</exception>
    public static TextureDescriptor Create(
        uint format,
        int width,
        int height,
        int pitch,
        ushort secondaryFormat = 0,
        uint formatParameter = 0) =>
        Validate(new TextureDescriptor(format, formatParameter, secondaryFormat, width, height, pitch));

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Width}x{Height} {Format} pitch={Pitch}{(HasMipChain ? $" mips={MipLevels}" : string.Empty)}";

    private static void Require(ReadOnlySpan<byte> data, int size, string what)
    {
        if (data.Length < size)
        {
            throw new TextureFormatException($"A {what} needs {size} bytes, got {data.Length}.");
        }
    }

    private static TextureDescriptor Validate(TextureDescriptor descriptor)
    {
        if (descriptor.Width <= 0 || descriptor.Height <= 0)
        {
            throw new TextureFormatException(
                $"Texture dimensions {descriptor.Width}x{descriptor.Height} are not positive.");
        }

        if (descriptor.Pitch <= 0)
        {
            throw new TextureFormatException($"Texture stride {descriptor.Pitch} is not positive.");
        }

        return descriptor;
    }
}
