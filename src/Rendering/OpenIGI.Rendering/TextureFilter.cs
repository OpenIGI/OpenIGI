namespace OpenIGI.Rendering;

/// <summary>How a texture is sampled between texels.</summary>
public enum TextureFilter
{
    /// <summary>Nearest-texel sampling. Correct for 1:1 UI blits, where filtering blurs the source art.</summary>
    Point,

    /// <summary>Bilinear sampling within a mip level, and between levels when the texture has mips.</summary>
    Linear,
}
