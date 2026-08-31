namespace OpenIGI.Rendering;

/// <summary>
/// What the frame shows instead of the scene, for looking at a pass rather than at the picture.
/// </summary>
/// <remarks>
/// Every effect from here to the end of the remaster track reads a buffer it does not draw, and a
/// buffer that is subtly wrong produces a picture that is plausibly wrong — a normal encoded in the
/// wrong space still lights, a motion vector with a flipped sign still reprojects. The only cheap way
/// to tell is to look at the buffer itself.
/// </remarks>
public enum DebugView
{
    /// <summary>The scene, tone mapped as usual.</summary>
    None = 0,

    /// <summary>Surface colour with no lighting at all.</summary>
    Albedo = 1,

    /// <summary>View-space normals, remapped so that each axis is a colour.</summary>
    Normal = 2,

    /// <summary>Depth, curved so that the near field is not all one shade.</summary>
    Depth = 3,

    /// <summary>Roughness in red, metallic in green, emissive in blue.</summary>
    Material = 4,

    /// <summary>
    /// Per-surface screen motion, scaled up hard because a frame's worth of it is a fraction of a
    /// pixel. Green is rightwards and red is downwards; a static world should be flat grey, and
    /// anywhere it is not is something the temporal resolve believes is moving.
    /// </summary>
    Motion = 5,

    /// <summary>The baked irradiance the original's lightmaps carry.</summary>
    BakedLight = 6,

    /// <summary>How much of the sky each surface can see, white for open and black for buried.</summary>
    Ao = 7,

    /// <summary>The colour neighbouring surfaces bounce onto each surface, with nothing else added.</summary>
    Ssgi = 8,

    /// <summary>What each surface reflects, with the scene under it removed.</summary>
    Ssr = 10,

    /// <summary>
    /// What the probe field alone says each surface receives, with no sun, no albedo and no occlusion.
    /// The one view that shows whether light is leaking through a wall, because leaked light is
    /// invisible once it has been multiplied by a dark surface.
    /// </summary>
    Probes = 9,

    /// <summary>
    /// The full lighting on a neutral grey material, the reference engine's "detail lighting". Base colour
    /// is replaced by a flat mid-grey so what remains is the light itself -- the sun, its shadows, the
    /// bounce and the sky fill, shaped by the surfaces' normals -- with the level's own textures taken out
    /// of the picture. The view for comparing this renderer's lighting against another's without the
    /// materials confusing the two.
    /// </summary>
    Lighting = 11,

    /// <summary>
    /// How much of the sun reaches each surface after shadowing, white for lit and black for occluded.
    /// The view that separates "the sun never arrives" from "the surface faces away", because both look
    /// identical once multiplied into the shaded scene.
    /// </summary>
    SunShadow = 12,

    /// <summary>
    /// The standing water: where it lies, and what shape its surface is. Red is how much of the pixel is
    /// under water and green and blue are the water's own normal, so a puddle that is not rippling and a
    /// puddle that is not there look nothing alike — which, once they are shaded, they very much do.
    /// </summary>
    Wetness = 13,

    /// <summary>
    /// The lamps' light alone, with the sun, the sky, the bounce and every surface's own colour taken
    /// out — only what the level's local lights put on each surface, AFTER their shadowing. The view that
    /// settles whether a lamp is lighting through a wall: if a surface the wall should hide is lit here,
    /// the occlusion missed it; if it is black, the light on it in the finished picture came from
    /// somewhere else.
    /// </summary>
    LampLight = 14,
}
