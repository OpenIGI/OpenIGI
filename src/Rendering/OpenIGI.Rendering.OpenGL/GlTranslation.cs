using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>How one RHI texture format is stored, uploaded and read in OpenGL.</summary>
/// <param name="Internal">The sized internal format the storage is allocated in.</param>
/// <param name="Format">The client format an upload or readback names.</param>
/// <param name="Type">The client type an upload or readback names.</param>
/// <param name="Compressed">Whether it is a block format, uploaded with the compressed calls.</param>
internal readonly record struct GlFormat(GLEnum Internal, GLEnum Format, GLEnum Type, bool Compressed = false);

/// <summary>How one vertex attribute is read.</summary>
/// <param name="Components">How many components.</param>
/// <param name="Type">The component type.</param>
/// <param name="Normalized">Whether an integer component is read as a fraction.</param>
/// <param name="Integer">Whether the attribute is an integer in the shader, which takes the <c>I</c> call.</param>
internal readonly record struct GlVertexFormat(int Components, GLEnum Type, bool Normalized, bool Integer);

/// <summary>The RHI's vocabulary in OpenGL's numbers.</summary>
/// <remarks>
/// Written as the values the specification gives them, named in a comment: Silk's enum spells the sized formats
/// several ways across releases, and the numbers are what the driver reads.
/// </remarks>
internal static class GlTranslation
{
    /// <summary>Gets how a texture format is stored.</summary>
    /// <param name="format">The format.</param>
    /// <returns>Its OpenGL form.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Not a format a texture can have.</exception>
    public static GlFormat Format(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm => new((GLEnum)0x8229 /* R8 */, (GLEnum)0x1903 /* RED */, (GLEnum)0x1401 /* UNSIGNED_BYTE */),
        RhiTextureFormat.Rg8Unorm => new((GLEnum)0x822B /* RG8 */, (GLEnum)0x8227 /* RG */, (GLEnum)0x1401),
        RhiTextureFormat.Rgba8Unorm => new((GLEnum)0x8058 /* RGBA8 */, (GLEnum)0x1908 /* RGBA */, (GLEnum)0x1401),
        RhiTextureFormat.Rgba8UnormSrgb => new((GLEnum)0x8C43 /* SRGB8_ALPHA8 */, (GLEnum)0x1908, (GLEnum)0x1401),

        // No BGRA storage in OpenGL: kept as RGBA and moved in and out as BGRA, which the driver swizzles. A shader
        // reads red as red either way, which is what a BGRA texture means everywhere else.
        RhiTextureFormat.Bgra8Unorm => new((GLEnum)0x8058, (GLEnum)0x80E1 /* BGRA */, (GLEnum)0x1401),
        RhiTextureFormat.Bgra8UnormSrgb => new((GLEnum)0x8C43, (GLEnum)0x80E1, (GLEnum)0x1401),
        RhiTextureFormat.R16Float => new((GLEnum)0x822D /* R16F */, (GLEnum)0x1903, (GLEnum)0x140B /* HALF_FLOAT */),
        RhiTextureFormat.Rg16Float => new((GLEnum)0x822F /* RG16F */, (GLEnum)0x8227, (GLEnum)0x140B),
        RhiTextureFormat.Rgba16Float => new((GLEnum)0x881A /* RGBA16F */, (GLEnum)0x1908, (GLEnum)0x140B),
        RhiTextureFormat.R32Float => new((GLEnum)0x822E /* R32F */, (GLEnum)0x1903, (GLEnum)0x1406 /* FLOAT */),
        RhiTextureFormat.Rg32Float => new((GLEnum)0x8230 /* RG32F */, (GLEnum)0x8227, (GLEnum)0x1406),
        RhiTextureFormat.Rgba32Float => new((GLEnum)0x8814 /* RGBA32F */, (GLEnum)0x1908, (GLEnum)0x1406),
        RhiTextureFormat.R32Uint => new((GLEnum)0x8236 /* R32UI */, (GLEnum)0x8D94 /* RED_INTEGER */, (GLEnum)0x1405 /* UNSIGNED_INT */),
        RhiTextureFormat.Rgb10A2Unorm => new((GLEnum)0x8059 /* RGB10_A2 */, (GLEnum)0x1908, (GLEnum)0x8368 /* UNSIGNED_INT_2_10_10_10_REV */),
        RhiTextureFormat.Rg11B10Ufloat => new((GLEnum)0x8C3A /* R11F_G11F_B10F */, (GLEnum)0x1907 /* RGB */, (GLEnum)0x8C3B /* UNSIGNED_INT_10F_11F_11F_REV */),
        RhiTextureFormat.Depth32Float => new((GLEnum)0x8CAC /* DEPTH_COMPONENT32F */, (GLEnum)0x1902 /* DEPTH_COMPONENT */, (GLEnum)0x1406),
        RhiTextureFormat.Depth24PlusStencil8 => new((GLEnum)0x88F0 /* DEPTH24_STENCIL8 */, (GLEnum)0x84F9 /* DEPTH_STENCIL */, (GLEnum)0x84FA /* UNSIGNED_INT_24_8 */),
        RhiTextureFormat.Depth16Unorm => new((GLEnum)0x81A5 /* DEPTH_COMPONENT16 */, (GLEnum)0x1902, (GLEnum)0x1403 /* UNSIGNED_SHORT */),
        RhiTextureFormat.Bc1RgbaUnorm => new((GLEnum)0x83F1 /* COMPRESSED_RGBA_S3TC_DXT1_EXT */, 0, 0, Compressed: true),
        RhiTextureFormat.Bc1RgbaUnormSrgb => new((GLEnum)0x8C4D /* COMPRESSED_SRGB_ALPHA_S3TC_DXT1_EXT */, 0, 0, Compressed: true),
        RhiTextureFormat.Bc7RgbaUnorm => new((GLEnum)0x8E8C /* COMPRESSED_RGBA_BPTC_UNORM */, 0, 0, Compressed: true),
        RhiTextureFormat.Bc7RgbaUnormSrgb => new((GLEnum)0x8E8D /* COMPRESSED_SRGB_ALPHA_BPTC_UNORM */, 0, 0, Compressed: true),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a texture format."),
    };

    /// <summary>Gets how a vertex attribute is read.</summary>
    /// <param name="format">The attribute's format.</param>
    /// <returns>Its OpenGL form.</returns>
    public static GlVertexFormat Vertex(RhiVertexFormat format) => format switch
    {
        RhiVertexFormat.Float32 => new(1, GLEnum.Float, false, false),
        RhiVertexFormat.Float32x2 => new(2, GLEnum.Float, false, false),
        RhiVertexFormat.Float32x3 => new(3, GLEnum.Float, false, false),
        RhiVertexFormat.Float32x4 => new(4, GLEnum.Float, false, false),
        RhiVertexFormat.Unorm8x4 => new(4, GLEnum.UnsignedByte, true, false),
        RhiVertexFormat.Uint8x4 => new(4, GLEnum.UnsignedByte, false, true),
        RhiVertexFormat.Uint32 => new(1, GLEnum.UnsignedInt, false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a vertex format."),
    };

    /// <summary>Gets a comparison function.</summary>
    public static GLEnum Compare(RhiCompare compare) => compare switch
    {
        RhiCompare.Never => GLEnum.Never,
        RhiCompare.Less => GLEnum.Less,
        RhiCompare.Equal => GLEnum.Equal,
        RhiCompare.LessEqual => GLEnum.Lequal,
        RhiCompare.Greater => GLEnum.Greater,
        RhiCompare.NotEqual => GLEnum.Notequal,
        RhiCompare.GreaterEqual => GLEnum.Gequal,
        _ => GLEnum.Always,
    };

    /// <summary>Gets a blend factor.</summary>
    public static GLEnum BlendFactor(RhiBlendFactor factor) => factor switch
    {
        RhiBlendFactor.Zero => GLEnum.Zero,
        RhiBlendFactor.One => GLEnum.One,
        RhiBlendFactor.Source => GLEnum.SrcColor,
        RhiBlendFactor.OneMinusSource => GLEnum.OneMinusSrcColor,
        RhiBlendFactor.SourceAlpha => GLEnum.SrcAlpha,
        RhiBlendFactor.OneMinusSourceAlpha => GLEnum.OneMinusSrcAlpha,
        RhiBlendFactor.Destination => GLEnum.DstColor,
        RhiBlendFactor.OneMinusDestination => GLEnum.OneMinusDstColor,
        RhiBlendFactor.DestinationAlpha => GLEnum.DstAlpha,
        _ => GLEnum.OneMinusDstAlpha,
    };

    /// <summary>Gets a blend equation.</summary>
    public static GLEnum BlendOperation(RhiBlendOperation operation) => operation switch
    {
        RhiBlendOperation.Add => GLEnum.FuncAdd,
        RhiBlendOperation.Subtract => GLEnum.FuncSubtract,
        RhiBlendOperation.ReverseSubtract => GLEnum.FuncReverseSubtract,
        RhiBlendOperation.Min => GLEnum.Min,
        _ => GLEnum.Max,
    };

    /// <summary>Gets a primitive mode.</summary>
    public static GLEnum Topology(RhiPrimitiveTopology topology) => topology switch
    {
        RhiPrimitiveTopology.PointList => GLEnum.Points,
        RhiPrimitiveTopology.LineList => GLEnum.Lines,
        RhiPrimitiveTopology.LineStrip => GLEnum.LineStrip,
        RhiPrimitiveTopology.TriangleStrip => GLEnum.TriangleStrip,
        _ => GLEnum.Triangles,
    };

    /// <summary>Gets a wrap mode.</summary>
    public static GLEnum Address(RhiAddressMode mode) => mode switch
    {
        RhiAddressMode.Repeat => GLEnum.Repeat,
        RhiAddressMode.MirrorRepeat => GLEnum.MirroredRepeat,
        _ => GLEnum.ClampToEdge,
    };

    /// <summary>Gets the minification filter for a texel filter and a mip filter, which OpenGL names as one.</summary>
    public static GLEnum MinFilter(RhiFilter texel, RhiFilter mip) => (texel, mip) switch
    {
        (RhiFilter.Nearest, RhiFilter.Nearest) => GLEnum.NearestMipmapNearest,
        (RhiFilter.Nearest, RhiFilter.Linear) => GLEnum.NearestMipmapLinear,
        (RhiFilter.Linear, RhiFilter.Nearest) => GLEnum.LinearMipmapNearest,
        _ => GLEnum.LinearMipmapLinear,
    };

    /// <summary>Gets the magnification filter.</summary>
    public static GLEnum MagFilter(RhiFilter filter) => filter == RhiFilter.Nearest ? GLEnum.Nearest : GLEnum.Linear;

    /// <summary>Gets the size of one index.</summary>
    public static (GLEnum Type, int Bytes) Index(RhiIndexFormat format) =>
        format == RhiIndexFormat.Uint16 ? (GLEnum.UnsignedShort, 2) : (GLEnum.UnsignedInt, 4);

    /// <summary>Gets the access a storage image binding is made with.</summary>
    public static GLEnum ImageAccess(RhiStorageAccess access) => access switch
    {
        RhiStorageAccess.ReadOnly => GLEnum.ReadOnly,
        RhiStorageAccess.WriteOnly => GLEnum.WriteOnly,
        _ => GLEnum.ReadWrite,
    };
}
