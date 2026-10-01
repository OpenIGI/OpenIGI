namespace OpenIGI.UI.Drawing;

/// <summary>How a drawn primitive combines with what is already behind it.</summary>
/// <remarks>
/// The interface's own blend vocabulary, so the framework never names a graphics backend's enum. Every
/// renderer maps it one-to-one: the game's sprite batch onto its pipeline blend states, the launcher's
/// Skia canvas onto <c>SKBlendMode</c>.
/// </remarks>
public enum UiBlend
{
    /// <summary>Source replaces destination; alpha is ignored.</summary>
    Opaque,

    /// <summary>Standard source-alpha blending: <c>src * srcA + dst * (1 - srcA)</c>.</summary>
    AlphaBlend,

    /// <summary>Additive blending: <c>src * srcA + dst</c>, for glows and light sweeps.</summary>
    Additive,
}
