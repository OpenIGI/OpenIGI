using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// A standalone <c>.tex</c> texture: the Innerloop <c>"LOOP"</c> magic, a version, a header whose
/// layout depends on that version, and an uncompressed mip chain.
/// </summary>
/// <remarks>
/// <para>
/// The original's loader (<c>Texture_LoadFile</c>, <c>0x4B24C0</c>) accepts exactly two versions —
/// <b>2</b> and <b>11</b> — and rejects everything else with <c>"'%s' is not a valid Texture file."</c>.
/// The version 9 files in the retail tree (the per-level <c>terrain.tex</c>) are not textures at all:
/// they are multi-frame sprites and go through the sprite loader instead.
/// </para>
/// <para>
/// Nothing here is compressed; the payload size is exactly the sum of the mip levels' strides. See
/// <c>docs/formats/tex-spr-fnt.md</c> and the original.
/// </para>
/// </remarks>
public static class TexFile
{
    /// <summary>The size of the magic and version prefix that precedes every <c>"LOOP"</c> header.</summary>
    public const int PrefixSize = 8;

    /// <summary>The older texture version: a 24-byte header that stores its stride directly.</summary>
    public const uint Version2 = 2;

    /// <summary>The newer texture version: a 32-byte header that stores an allocated width and a byte depth.</summary>
    public const uint Version11 = 11;

    /// <summary>Gets the Innerloop raw-asset magic shared by <c>.tex</c> and <c>.spr</c>.</summary>
    public static FourCc Magic { get; } = FourCc.FromString("LOOP");

    /// <summary>Determines whether <paramref name="data"/> starts with the <c>"LOOP"</c> magic.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> when the first four bytes are <c>LOOP</c>.</returns>
    public static bool IsLoopAsset(ReadOnlySpan<byte> data) =>
        data.Length >= PrefixSize && FourCc.FromBytes(data) == Magic;

    /// <summary>Reads the version word of a <c>"LOOP"</c> asset.</summary>
    /// <param name="data">The asset bytes.</param>
    /// <returns>The version.</returns>
    /// <exception cref="TextureFormatException">The bytes do not start with the <c>"LOOP"</c> magic.</exception>
    public static uint ReadVersion(ReadOnlySpan<byte> data)
    {
        if (!IsLoopAsset(data))
        {
            throw new TextureFormatException("Not a 'LOOP' asset: the magic does not match.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
    }

    /// <summary>Determines whether <paramref name="data"/> is a version the texture loader accepts.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns><see langword="true"/> for a version 2 or version 11 <c>"LOOP"</c> asset.</returns>
    public static bool IsTexture(ReadOnlySpan<byte> data)
    {
        if (!IsLoopAsset(data))
        {
            return false;
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        return version is Version2 or Version11;
    }

    /// <summary>Reads the header of a <c>.tex</c> file and reports where its payload starts.</summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="payloadOffset">Receives the offset of the first payload byte.</param>
    /// <returns>The normalised descriptor.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a usable <c>.tex</c> file.</exception>
    public static TextureDescriptor ReadHeader(ReadOnlySpan<byte> data, out int payloadOffset)
    {
        uint version = ReadVersion(data);
        switch (version)
        {
            case Version2:
                payloadOffset = TextureDescriptor.TexVersion2HeaderSize;
                return TextureDescriptor.ReadTexVersion2(data);
            case Version11:
                payloadOffset = TextureDescriptor.TexVersion11HeaderSize;
                return TextureDescriptor.ReadTexVersion11(data);
            default:
                throw new TextureFormatException(
                    $"Texture version {version} is not one the original loads; it accepts only {Version2} and {Version11}.");
        }
    }

    /// <summary>Loads and decodes a <c>.tex</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="palette">The palette to use if the texture is paletted.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a usable <c>.tex</c> file.</exception>
    public static TextureImage Load(ReadOnlySpan<byte> data, ReadOnlySpan<Bgra32> palette = default)
    {
        TextureDescriptor descriptor = ReadHeader(data, out int payloadOffset);
        return TextureDecoder.Decode(descriptor, data[payloadOffset..], palette);
    }
}
