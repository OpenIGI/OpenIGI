namespace OpenIGI.Rendering;

/// <summary>
/// How high-dynamic-range light is mapped onto what a display can show.
/// </summary>
/// <remarks>
/// The scene is shaded in linear light with no upper bound — a muzzle flash, the sun through fog, a
/// specular highlight on wet metal all exceed white — and something has to decide what "white" means
/// before it reaches the screen. That decision is the single biggest lever on how the game reads, so
/// it is a setting rather than a constant.
/// </remarks>
public enum ToneMapOperator
{
    /// <summary>
    /// Clamp to <c>[0, 1]</c> and present. Not a tone map at all.
    /// </summary>
    /// <remarks>
    /// The identity, and the only value under which the offscreen path can be pixel-identical to the
    /// original's direct-to-backbuffer output. That is what makes it the regression gate's setting
    /// rather than a look anyone would choose.
    /// </remarks>
    None,

    /// <summary>A gentle filmic shoulder that leaves midtones where the original put them.</summary>
    /// <remarks>
    /// The default. Chosen so that turning the modern pipeline on does not by itself restage every
    /// level: it rolls off what would have clipped and otherwise stays out of the way.
    /// </remarks>
    Neutral,

    /// <summary>The ACES filmic curve, in Narkowicz's fitted form.</summary>
    /// <remarks>Contrastier, with the saturated highlight roll-off people read as "cinematic".</remarks>
    Aces,

    /// <summary>The AgX curve.</summary>
    /// <remarks>
    /// Desaturates as it approaches white rather than hue-shifting into it, which keeps a bright sky
    /// or a fog bank from turning cyan. The most forgiving of the three with very bright volumetrics.
    /// </remarks>
    Agx,
}
