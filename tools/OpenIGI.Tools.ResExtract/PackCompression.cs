using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Tools.ResExtract;

/// <summary>
/// Turns a pack's world texture into the stored block-compressed chain the game uploads as it is.
/// </summary>
/// <remarks>
/// <para>
/// Everything the loader does to a PNG on its way to the GPU happens here instead, once, in the same order
/// and against the same cutoff: a cut-out is classified from level zero before its alpha is touched, its
/// alpha is widened, the chain is built (rebuilt, for a cut-out) by the alpha-weighted filter, and the
/// chain's coverage is preserved level by level. The result is encoded, and the game does none of it.
/// </para>
/// <para>
/// BC1 where the chain is fully opaque, at half a byte a texel; BC7 where it has any alpha, at one. The
/// derived surface map is four channels of data -- a normal's x and y, roughness, occlusion -- so it is
/// BC7, built by the linear chain the device used to build it with.
/// </para>
/// </remarks>
internal static class PackCompression
{
    /// <summary>The alpha the game's shaders test cut-outs against; the loader's own constant.</summary>
    /// <remarks>Must equal <c>TextureDecodeCache.AlphaTestCutoff</c>, which this tool cannot reference.</remarks>
    private const float AlphaTestCutoff = 0.5f;

    /// <summary>The largest proxy kept for what reads a texture's pixels on the CPU.</summary>
    private const int ProxyEdge = 128;

    /// <summary>Whether a picture can be stored compressed.</summary>
    /// <param name="image">The picture.</param>
    /// <returns><see langword="true"/> when both sides are whole blocks.</returns>
    /// <remarks>
    /// A browser's WebGPU refuses a block-compressed texture whose top level is not a whole number of
    /// blocks, so anything else stays a PNG -- a handful of odd-sized art, never a world surface of a 4x pack.
    /// </remarks>
    public static bool CanCompress(TextureImage image) =>
        image.Width % BlockCompression.BlockSize == 0 && image.Height % BlockCompression.BlockSize == 0;

    /// <summary>Compresses one world texture's frame.</summary>
    /// <param name="frame">The frame, its image a single uncompressed level.</param>
    /// <param name="quality">How hard the encoder searches.</param>
    /// <returns>The same frame, carrying the stored chains and a small proxy as its image.</returns>
    public static PackPicture.Frame Compress(in PackPicture.Frame frame, BlockQuality quality)
    {
        TextureImage image = frame.Image;

        bool cutout = AlphaCoverage.IsCutout(image.GetLevel(0));
        image.WidenCutoutAlpha(AlphaTestCutoff);
        image = cutout ? image.RebuildMipChain() : image.WithMipChain();
        image.PreserveAlphaCoverage(AlphaTestCutoff);

        BlockFormat format = !cutout && IsOpaque(image.Pixels) ? BlockFormat.Bc1 : BlockFormat.Bc7;
        CompressedTextureImage picture = new(
            format,
            image.Width,
            image.Height,
            image.MipLevels,
            EncodeChain(image, format, quality),
            cutout,
            Proxy(image));

        CompressedTextureImage? surface = null;
        if (frame.Surface is { } map && CanCompress(map))
        {
            (Bgra32[] pixels, int levels) = LinearMipChain.Build(map.GetLevel(0), map.Width, map.Height);
            surface = new CompressedTextureImage(
                BlockFormat.Bc7,
                map.Width,
                map.Height,
                levels,
                EncodeLinearChain(pixels, map.Width, map.Height, levels, quality),
                isCutout: false,
                proxy: null);
        }

        return frame with
        {
            Image = picture.Proxy ?? image,
            Surface = null,
            Compressed = picture,
            CompressedSurface = surface,
        };
    }

    private static bool IsOpaque(ReadOnlySpan<Bgra32> pixels)
    {
        foreach (Bgra32 texel in pixels)
        {
            if (texel.A != 255)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] EncodeChain(TextureImage image, BlockFormat format, BlockQuality quality)
    {
        using MemoryStream chain = new();
        for (int level = 0; level < image.MipLevels; level++)
        {
            (int width, int height) = image.GetLevelSize(level);
            chain.Write(BlockCodec.Encode(image.GetLevel(level), width, height, format, quality));
        }

        return chain.ToArray();
    }

    private static byte[] EncodeLinearChain(Bgra32[] pixels, int width, int height, int levels, BlockQuality quality)
    {
        using MemoryStream chain = new();
        int at = 0;
        for (int level = 0; level < levels; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            int count = levelWidth * levelHeight;
            chain.Write(BlockCodec.Encode(pixels.AsSpan(at, count), levelWidth, levelHeight, BlockFormat.Bc7, quality));
            at += count;
        }

        return chain.ToArray();
    }

    /// <summary>The first level of the chain no larger than the proxy edge, copied out on its own.</summary>
    private static TextureImage Proxy(TextureImage chain)
    {
        for (int level = 0; level < chain.MipLevels; level++)
        {
            (int width, int height) = chain.GetLevelSize(level);
            if (Math.Max(width, height) <= ProxyEdge || level == chain.MipLevels - 1)
            {
                return TextureImage.FromPixels(width, height, chain.GetLevel(level).ToArray());
            }
        }

        return chain;
    }
}
