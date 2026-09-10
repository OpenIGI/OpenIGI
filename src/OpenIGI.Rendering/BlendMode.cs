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

    /// <summary>Doubled modulation: <c>src * dst + dst * src</c>, which is <c>2 * src * dst</c>.</summary>
    /// <remarks>
    /// I.G.I. 2's terrain lightmap pass. <c>TerrainRender_SetBlendMode</c> (<c>igi2ed 0x666B90</c>)
    /// case 3 sets <c>SRCBLEND = D3DBLEND_DESTCOLOR</c> with <c>DESTBLEND = D3DBLEND_SRCCOLOR</c>, which
    /// the blender evaluates as the sum of the two products — a MODULATE2X. It is why a terrain
    /// lightmap's neutral is a half rather than white: the doubling is in the blend, not in the texels.
    /// </remarks>
    Modulate2X,
}
