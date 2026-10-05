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
    /// The full lighting on a neutral grey material, a "lighting only" view. Base colour is replaced by a
    /// flat mid-grey so what remains is the light itself -- the sun, its shadows, the bounce and the sky
    /// fill, shaped by the surfaces' normals -- with the level's own textures taken out of the picture. The
    /// view for comparing this renderer's lighting against another's without the materials confusing the
    /// two.
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

    /// <summary>
    /// The temporal pass's parallax verdict: white where the reprojected history held this surface,
    /// black where it was hidden behind something nearer and the history restarts.
    /// </summary>
    UpscaleParallax = 15,

    /// <summary>
    /// The temporal pass's shading verdict: white where the history's shading still agrees with this
    /// frame, black where it has been contradicted and the history is clamped or rebuilt.
    /// </summary>
    UpscaleShading = 16,

    /// <summary>
    /// How many frames' worth of samples each history texel holds, as a fraction of the ceiling: white
    /// is a fully converged history, black a texel that restarted this frame.
    /// </summary>
    UpscaleHistory = 17,

    /// <summary>
    /// The G-buffer's roughness alone, black for a mirror and white for fully matte. The view that
    /// settles whether a derived roughness map is reaching the shader at all, because once it has been
    /// through a GGX lobe a twelve-per-cent spread at the matte end is invisible.
    /// </summary>
    Roughness = 18,

    /// <summary>
    /// The G-buffer's metallic alone, black for a dielectric and white for bare metal. Expected to be
    /// flat black everywhere the level has no authored metal, which is how a channel that is carrying
    /// nothing is told apart from one that is carrying zero on purpose.
    /// </summary>
    Metallic = 19,

    /// <summary>
    /// The material occlusion the base colour's alpha carries, white for open and black for a crease.
    /// Distinct from <see cref="Ao"/>, which is the screen-space horizon search: this one is baked into
    /// the texture and is the same in every frame from every angle.
    /// </summary>
    MaterialAo = 20,

    /// <summary>
    /// What the level's glazing reflects, on its own, with the scene under it removed and BEFORE the
    /// Fresnel term is applied.
    /// </summary>
    /// <remarks>
    /// The one view that tells the three ways glass can come out wrong apart, which nothing in the final
    /// picture can: a reflection is a few per cent of a lit pixel head-on, so all three look identical
    /// there. Nothing visible anywhere means the panes never reached the layer at all — the raster half
    /// is not writing it. Panes visible as flat black shapes means the layer is right and the trace found
    /// nothing to resolve. Panes visible carrying the room means both halves work and the only thing left
    /// between here and the picture is the Fresnel weight.
    /// </remarks>
    Glass = 21,

    /// <summary>
    /// Which ROUTE each pane pixel's reflection came from, as a flat colour per route rather than as
    /// light.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Glass"/> says whether a reflection arrived; this says where it came from, and the two
    /// answer different questions. A pane can be carrying the room convincingly and still be resolving
    /// almost every pixel out of the surface cache, because a card of a wall looks like that wall — just
    /// flatter, coarser and lit by a bake rather than by this frame. That is invisible in the finished
    /// picture and invisible in the raw view, and it is the whole difference between a window that can
    /// show a person standing at it and one that never can.
    /// </para>
    /// <para>
    /// Green is the LIT FRAME: full detail, this frame's shadows, and the only route by which anything
    /// that moves — a soldier, the weapon, the player's own body — can appear in glass at all. Blue is a
    /// surface-cache CARD: static geometry only. Red is a ray that struck something solid neither could
    /// put a colour to, which is a body the camera cannot see. Grey is the SKY on a clean miss.
    /// </para>
    /// <para>
    /// Mostly blue means the frame route is being rejected and everything is falling through to the
    /// cards, which is a bug in the resolve rather than in the trace.
    /// </para>
    /// </remarks>
    GlassRoute = 22,

    /// <summary>
    /// The TEXTURE a reflected ray found at the point it struck, with no lighting applied to it at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reflected body that comes out flat and colourless has two causes that are indistinguishable in
    /// the finished picture, and this is what separates them. If the body appears here carrying its own
    /// colours and markings, the texture reached the shader and the LIGHTING crushed it. If it appears as
    /// a uniform mid-grey, that grey is the albedo array's own clear value, and the layer the hit named
    /// was never written — a registration problem, not a lighting one.
    /// </para>
    /// <para>
    /// Only the material route writes anything here; a pixel resolved from the lit frame or from a card
    /// is black, because neither of those ever reads a texture.
    /// </para>
    /// </remarks>
    GlassAlbedo = 23,

    /// <summary>The DIRECTION each pane pixel reflects in, as a colour.</summary>
    /// <remarks>
    /// Red is east, green is north, blue is UP, and each is centred on mid-grey so a negative component
    /// reads dark — so a pane reflecting the ground is dark blue and one reflecting the sky is bright.
    /// The question it answers is the one no other view can: whether a reflection that shows the wrong
    /// part of the world is aiming somewhere wrong, or aiming correctly and failing to find what is
    /// there. Those have nothing in common and look identical in every other view.
    /// </remarks>
    GlassRay = 24,

    /// <summary>The pane's own world normal, already turned to face the camera.</summary>
    /// <remarks>
    /// One step behind <see cref="GlassRay"/>: the aim is twice the normal's error, so a reflection that
    /// points somewhere impossible is nearly always this. Blue is the UP component, and a vertical pane
    /// should have almost none of it — a window whose normal tilts is a window reflecting the sky where
    /// it should reflect the ground.
    /// </remarks>
    GlassNormal = 25,

    /// <summary>
    /// How much of each surface's lamp light gets past what stands in its way, after the denoisers: white
    /// where all of it arrives, black where none does, red where no lamp reaches the surface at all.
    /// </summary>
    /// <remarks>
    /// The traced ratio itself, before any lamp's colour, distance or facing multiplies it -- the one view
    /// that separates a lamp that is shadowed from a lamp that is simply far away or pointed elsewhere,
    /// which look the same once shaded.
    /// </remarks>
    LampShadow = 26,

    /// <summary>The player's flashlight alone: what its beam puts on each surface, shadowed, and nothing else.</summary>
    /// <remarks>
    /// The beam's shape on the world -- its hot core, its rim and its spill -- with the rest of the frame's
    /// light taken away, for tuning the cookie without the room's own light in the way.
    /// </remarks>
    FlashlightLight = 27,

    /// <summary>How much of the flashlight reaches each surface: white where it all arrives, black in shadow.</summary>
    /// <remarks>The mask the shading multiplies the beam by, after its filter; white everywhere without one.</remarks>
    FlashlightShadow = 28,

    /// <summary>
    /// What the software lamp march decided, where lamp shadows are marched through the distance field:
    /// red for the share of rays it stopped, green for the share it let through past a surface it was
    /// touching -- a receiver's own plane, a fitting, a slab the plane tests took for one of those.
    /// </summary>
    /// <remarks>
    /// The view that tells a leak through a thin canopy (green over the leak: the field held the slab and
    /// a test waved it through) from a slab the field never held at all (black: nothing near the ray). Black
    /// everywhere with ray tracing on, which does not march.
    /// </remarks>
    LampMarch = 29,

    /// <summary>
    /// Which answer each software lamp-shadow ray got: red where a lamp's own cube of shadow maps held it
    /// shadowed, green where its map held it lit, blue where the lamp had no map covering the point and
    /// the distance field was marched instead.
    /// </summary>
    /// <remarks>
    /// The view that tells a lamp whose map is wrong (red over a wall it plainly lights) from one that has
    /// no map at all (blue: the coarse field decided), and both from a lamp whose map is right. Black
    /// everywhere with ray tracing on, which reads no maps.
    /// </remarks>
    LampMaps = 30,
}
