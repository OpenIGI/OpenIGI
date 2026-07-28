namespace OpenIGI.Formats.World;

/// <summary>One image inside an <c>.olm</c> object lightmap.</summary>
/// <remarks>
/// Dimensions are powers of two, from 1×1 up to 256×256, and need not be square — 36 distinct sizes occur
/// across the retail corpus. The channel order is the same open question as for <c>.tex</c> textures and
/// is settled by the renderer, not here.
/// </remarks>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Pixels">The pixel bytes, four per pixel, as a slice of the source buffer.</param>
public readonly record struct ObjectLightmapImage(int Width, int Height, ReadOnlyMemory<byte> Pixels)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Width}x{Height}";
}
