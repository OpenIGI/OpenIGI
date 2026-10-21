namespace OpenIGI.Formats.Textures;

/// <summary>
/// The pixel format a texture payload is stored in, as the original's <c>DownloadTexture</c> switch
/// selects it.
/// </summary>
/// <remarks>
/// <para>
/// The values are the low bits of the <c>TEXH</c> header's first dword. Bit <c>0x40</c> of that dword
/// is a separate mip-chain flag and is not part of the format code — see
/// <see cref="TextureDescriptor.HasMipChain"/>.
/// </para>
/// <para>
/// Formats 3 and 4 decode identically, by a straight dword copy; the original distinguishes them only
/// when choosing video-memory storage.
/// </para>
/// </remarks>
public enum TextureFormatCode
{
    /// <summary>Eight-bit indices into the sprite's palette.</summary>
    Palette8 = 1,

    /// <summary>Sixteen-bit <c>A1R5G5B5</c>: one alpha bit and five bits per colour channel.</summary>
    A1R5G5B5 = 2,

    /// <summary>Thirty-two-bit <c>0xAARRGGBB</c>, stored byte-wise as blue, green, red, alpha.</summary>
    Bgra8 = 3,

    /// <summary>Thirty-two-bit <c>0xAARRGGBB</c>, the format-4 variant. Decodes exactly like <see cref="Bgra8"/>.</summary>
    Bgra8Alternate = 4,

    /// <summary>Sixteen-bit <c>R5G6B5</c>, always opaque.</summary>
    R5G6B5 = 5,
}
