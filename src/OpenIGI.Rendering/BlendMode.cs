namespace OpenIGI.Rendering;

/// <summary>How a draw call's fragments combine with what is already in the render target.</summary>
public enum BlendMode
{
    /// <summary>Source replaces destination; alpha is ignored.</summary>
    Opaque,

    /// <summary>Standard source-alpha blending: <c>src * srcA + dst * (1 - srcA)</c>.</summary>
    AlphaBlend,

    /// <summary>Additive blending: <c>src * srcA + dst</c>. Used by muzzle flashes, glows and lens effects.</summary>
    Additive,
}
