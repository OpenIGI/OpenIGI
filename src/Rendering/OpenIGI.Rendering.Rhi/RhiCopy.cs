namespace OpenIGI.Rendering.Rhi;

/// <summary>A size in texels.</summary>
/// <param name="Width">Along x.</param>
/// <param name="Height">Along y.</param>
/// <param name="DepthOrArrayLayers">Along z, or how many layers.</param>
public readonly record struct RhiExtent3D(int Width, int Height, int DepthOrArrayLayers = 1);

/// <summary>A position in texels.</summary>
/// <param name="X">Along x.</param>
/// <param name="Y">Along y.</param>
/// <param name="Z">Along z, or which layer.</param>
public readonly record struct RhiOrigin3D(int X = 0, int Y = 0, int Z = 0);

/// <summary>Which part of a texture a copy touches.</summary>
/// <param name="Texture">The texture.</param>
/// <param name="MipLevel">Which mip level.</param>
/// <param name="Origin">Where in it the region starts.</param>
public readonly record struct RhiTextureRegion(
    IRhiTexture Texture,
    int MipLevel = 0,
    RhiOrigin3D Origin = default);

/// <summary>How texels are laid out in a buffer either side of a copy.</summary>
/// <param name="Buffer">The buffer.</param>
/// <param name="Offset">Where the texels start.</param>
/// <param name="BytesPerRow">
/// How many bytes one row occupies. WebGPU requires a multiple of 256 for a copy that has more than one
/// row, so a readback pads its rows and the host un-pads them; Vulkan accepts the padding too, which is
/// why one number serves both.
/// </param>
/// <param name="RowsPerImage">How many rows one image occupies, for a copy spanning layers or depth.</param>
public readonly record struct RhiBufferTextureCopy(
    IRhiBuffer Buffer,
    ulong Offset,
    int BytesPerRow,
    int RowsPerImage = 0)
{
    /// <summary>The alignment WebGPU requires of <see cref="BytesPerRow"/> in a multi-row copy.</summary>
    public const int RequiredRowAlignment = 256;

    /// <summary>Rounds a row's width up to the alignment a copy requires.</summary>
    /// <param name="bytesPerRow">The unpadded width in bytes.</param>
    /// <returns>The padded width.</returns>
    public static int AlignRow(int bytesPerRow) =>
        ((bytesPerRow + RequiredRowAlignment - 1) / RequiredRowAlignment) * RequiredRowAlignment;
}
