namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// What one texel holds.
/// </summary>
/// <remarks>
/// <para>
/// The formats this renderer actually uses, and no others. A longer list would be a list of things a
/// pass could ask for and a backend could not provide: WebGPU's format table is shorter than Vulkan's,
/// its storage-texture support shorter still, and a format offered here that one backend cannot write
/// to is a pass that works on one machine and not the other.
/// </para>
/// <para>
/// <b>Colour space is part of the format.</b> The sRGB pairs below are not an encoding detail to be
/// applied later: a view created as sRGB has the hardware convert on every read and write, and one
/// created without it does not. Mixing them up is the single most common way for a renderer to come out
/// uniformly washed out or uniformly dark.
/// </para>
/// </remarks>
public enum RhiTextureFormat
{
    /// <summary>No format. Not a valid texture; the default so an unset field is caught.</summary>
    Undefined = 0,

    /// <summary>One unsigned byte, normalised. Masks, coverage and the distance field.</summary>
    R8Unorm,

    /// <summary>Two unsigned bytes, normalised.</summary>
    Rg8Unorm,

    /// <summary>Four unsigned bytes, normalised.</summary>
    Rgba8Unorm,

    /// <summary>Four unsigned bytes, normalised, read and written through sRGB.</summary>
    Rgba8UnormSrgb,

    /// <summary>Four unsigned bytes in blue-green-red-alpha order, which is what a swapchain offers.</summary>
    Bgra8Unorm,

    /// <summary>The same, through sRGB.</summary>
    Bgra8UnormSrgb,

    /// <summary>One 16-bit float.</summary>
    R16Float,

    /// <summary>Two 16-bit floats. Screen motion, and anything else that is a signed pair.</summary>
    Rg16Float,

    /// <summary>Four 16-bit floats. What everything holding light is stored in.</summary>
    Rgba16Float,

    /// <summary>One 32-bit float. Depth read back as colour, and single-channel accumulation.</summary>
    R32Float,

    /// <summary>Two 32-bit floats.</summary>
    Rg32Float,

    /// <summary>Four 32-bit floats.</summary>
    Rgba32Float,

    /// <summary>One 32-bit unsigned integer. What an atomic minimum over depth bits is stored in.</summary>
    R32Uint,

    /// <summary>Ten bits each of red, green and blue with two of alpha: a wide-gamut present format.</summary>
    Rgb10A2Unorm,

    /// <summary>Eleven bits each of red and green and ten of blue, unsigned float: cheap HDR colour.</summary>
    Rg11B10Ufloat,

    /// <summary>A 32-bit float depth buffer.</summary>
    Depth32Float,

    /// <summary>A 24-bit normalised depth buffer with eight bits of stencil.</summary>
    Depth24PlusStencil8,

    /// <summary>A 16-bit normalised depth buffer.</summary>
    Depth16Unorm,

    /// <summary>BC1: eight bytes per four-by-four block, colour with one bit of alpha. Opaque surfaces.</summary>
    Bc1RgbaUnorm,

    /// <summary>BC1, read through sRGB.</summary>
    Bc1RgbaUnormSrgb,

    /// <summary>BC7: sixteen bytes per four-by-four block, colour and full alpha at near-lossless quality.</summary>
    Bc7RgbaUnorm,

    /// <summary>BC7, read through sRGB.</summary>
    Bc7RgbaUnormSrgb,
}

/// <summary>What a format is for, which decides where it may be attached or bound.</summary>
public static class RhiTextureFormatExtensions
{
    /// <summary>Whether a format holds depth, and so attaches as a depth target rather than a colour one.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns><see langword="true"/> for a depth or depth-stencil format.</returns>
    public static bool IsDepth(this RhiTextureFormat format) => format
        is RhiTextureFormat.Depth32Float
        or RhiTextureFormat.Depth24PlusStencil8
        or RhiTextureFormat.Depth16Unorm;

    /// <summary>Whether a format carries stencil as well as depth.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns><see langword="true"/> when it has a stencil aspect.</returns>
    public static bool HasStencil(this RhiTextureFormat format) =>
        format is RhiTextureFormat.Depth24PlusStencil8;

    /// <summary>How many bytes one texel of a format occupies.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns>The size in bytes, or zero for a format with no fixed size.</returns>
    /// <remarks>
    /// Needed by both backends and for the same reason: a copy between a buffer and a texture states its
    /// row pitch, and one of them wants that pitch in TEXELS. Deriving the texel size by dividing a
    /// padded row by its width gives the padding back instead, which reads as a copy that works for the
    /// first row and returns nothing for the rest.
    /// </remarks>
    public static int BytesPerTexel(this RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm => 1,
        RhiTextureFormat.Rg8Unorm => 2,
        RhiTextureFormat.R16Float => 2,
        RhiTextureFormat.Rg16Float => 4,
        RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba8UnormSrgb => 4,
        RhiTextureFormat.Bgra8Unorm or RhiTextureFormat.Bgra8UnormSrgb => 4,
        RhiTextureFormat.R32Float or RhiTextureFormat.R32Uint => 4,
        RhiTextureFormat.Rgb10A2Unorm or RhiTextureFormat.Rg11B10Ufloat => 4,
        RhiTextureFormat.Rgba16Float or RhiTextureFormat.Rg32Float => 8,
        RhiTextureFormat.Rgba32Float => 16,
        RhiTextureFormat.Depth16Unorm => 2,
        RhiTextureFormat.Depth32Float or RhiTextureFormat.Depth24PlusStencil8 => 4,
        _ => 0,
    };

    /// <summary>Whether reads and writes convert between sRGB and linear.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns><see langword="true"/> when the hardware converts.</returns>
    public static bool IsSrgb(this RhiTextureFormat format) => format
        is RhiTextureFormat.Rgba8UnormSrgb
        or RhiTextureFormat.Bgra8UnormSrgb
        or RhiTextureFormat.Bc1RgbaUnormSrgb
        or RhiTextureFormat.Bc7RgbaUnormSrgb;

    /// <summary>Whether a format stores four-by-four blocks rather than texels.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns><see langword="true"/> for a BC format.</returns>
    /// <remarks>
    /// A block format has no bytes per texel -- <see cref="BytesPerTexel"/> answers zero -- and every copy
    /// into one is stated in whole blocks: rows of <see cref="BlockBytes"/> per four texels across, four
    /// texels deep.
    /// </remarks>
    public static bool IsBlockCompressed(this RhiTextureFormat format) => format
        is RhiTextureFormat.Bc1RgbaUnorm
        or RhiTextureFormat.Bc1RgbaUnormSrgb
        or RhiTextureFormat.Bc7RgbaUnorm
        or RhiTextureFormat.Bc7RgbaUnormSrgb;

    /// <summary>How many bytes one four-by-four block of a block format occupies.</summary>
    /// <param name="format">The format asked about.</param>
    /// <returns>Eight for BC1, sixteen for BC7, zero for a format that is not block-compressed.</returns>
    public static int BlockBytes(this RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.Bc1RgbaUnorm or RhiTextureFormat.Bc1RgbaUnormSrgb => 8,
        RhiTextureFormat.Bc7RgbaUnorm or RhiTextureFormat.Bc7RgbaUnormSrgb => 16,
        _ => 0,
    };
}

/// <summary>One component of a vertex, as the vertex buffer holds it.</summary>
/// <remarks>The set the mesh, sprite and outline pipelines between them declare, and no more.</remarks>
public enum RhiVertexFormat
{
    /// <summary>No format. Not valid; the default so an unset field is caught.</summary>
    Undefined = 0,

    /// <summary>One 32-bit float.</summary>
    Float32,

    /// <summary>Two 32-bit floats.</summary>
    Float32x2,

    /// <summary>Three 32-bit floats.</summary>
    Float32x3,

    /// <summary>Four 32-bit floats.</summary>
    Float32x4,

    /// <summary>Four unsigned bytes, normalised to [0, 1].</summary>
    Unorm8x4,

    /// <summary>Four unsigned bytes, kept as integers.</summary>
    Uint8x4,

    /// <summary>One 32-bit unsigned integer.</summary>
    Uint32,
}

/// <summary>How wide one index is.</summary>
public enum RhiIndexFormat
{
    /// <summary>Sixteen bits per index.</summary>
    Uint16,

    /// <summary>Thirty-two bits per index.</summary>
    Uint32,
}
