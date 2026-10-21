using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the screen probe gather and the world-space radiance cache it falls back on.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class ScreenGatherCVars
{
    /// <summary>How bright a single screen-traced ray may come back, before it feeds the next frame.</summary>
    /// <remarks>
    /// The screen trace resolves a hit from the previous frame's lit scene, and this frame's indirect is
    /// built out of what it returns — which then lights the scene the next trace reads. Unbounded, that
    /// loop does not settle: past a certain gather strength each pass is brighter than the last, so the
    /// read is clamped.
    /// <para>
    /// In EXPOSED units, so the cap means the same brightness on screen by day and by night, and at ten on
    /// every preset. The modern presets carried 50.1 applied to raw scene radiance, which at night --
    /// exposure sixteen and up -- let a ray through some eighty times brighter than ten exposed: the few of
    /// a probe's sixteen rays that landed on a lamp's fixture or its pool of light swung its estimate frame
    /// to frame, and that was the cloud drifting around every lamp at night.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> ScreenGatherMaxIntensity = Presets.Define(
        CVar.Float(
            "r_screen_gi_max",
            10f,
            0.1f,
            1000f,
            CVarOptions.Archive,
            "Ceiling on a single screen GI ray's radiance as the eye sees it (after exposure); stops the gather feeding itself."),
        vanilla: "10",
        enhanced: "10",
        high: "10",
        ultra: "10");

    /// <summary>How strongly the screen probe gather's indirect light fills the deferred lighting.</summary>
    /// <remarks>
    /// Zero is off, which is vanilla alone: above zero, the gather traces the surface cache per
    /// screen probe into an atlas and the deferred lighting reads it as its diffuse fill over the far-field
    /// floor -- the stage where the traced global illumination reaches the screen. Not a name
    /// clash with <see cref="GlobalIlluminationCVars.GlobalIllumination"/> (<c>r_gi</c>), which is the baked probe field; this is
    /// the live screen-space gather, and it needs the surface cache and the distance fields, so it does
    /// nothing without both. The magnitude compensates for how the gather reads the cache: the surface
    /// cache's final atlas holds each card's OUTGOING radiance -- the incident light already taken through
    /// the surface's albedo and the Lambertian one-over-pi -- and the deferred fill then takes the
    /// receiver's albedo again, so the raw gather lands roughly an order of magnitude below the ambient it
    /// stands in for. A strength around two and a half lifts it back, so a shadowed wall reads as lit
    /// concrete rather than black. It stays a knob while the look is tuned.
    /// </remarks>
    public static readonly NumericCVar<float> ScreenGather = Presets.Define(
        CVar.Float(
            "r_screen_gi",
            0f,
            0f,
            8f,
            CVarOptions.Archive,
            "Strength of the screen probe gather's indirect light in the deferred fill. 0 is off."),
        vanilla: "0",
        enhanced: "1",
        high: "1",

        // On at every preset above vanilla. The screen probe gather is a real-time indirect -- it traces the surface cache along
        // the view so the bounce responds to the frame -- and it is what carries the warm bounced light the
        // interior reads as: the world-space radiance cache alone, sampled straight, comes back markedly
        // cooler than the target look (its far-field trace misses the near, sun-warmed bounce a screen ray
        // catches), so the gather is not optional for the look, it is the look. Two things make it shippable
        // where the raw screen estimate was not, both added with it. Its full-resolution temporal resolve
        // now clamps each reprojected history into the current frame's own neighbourhood mean and spread --
        // a standard anti-shimmer clamp -- so a stale or noisy history can no longer slide across a wall as
        // the low-frequency shimmer that once shelved it. And the gather REPLACES the flat ambient, the
        // far probe field and the additive sky term rather than being floored against them -- flooring
        // against an unoccluded ambient is what washes an interior flat -- which is also why the bounce
        // it carries has to be whole: with r_gi_bounce below one there is nothing behind the gather to
        // make up the difference, and the fill it drops comes straight off the frame. One, because the
        // gather returns physically-normalised irradiance -- the pi-over-N estimator with the sky
        // integrated ray by ray -- and the deferred fill takes it through the Lambert response. Anything
        // other than one is an artistic bias on real light transport, not a calibration.
        ultra: "1");

    /// <summary>Whether the world-space radiance cache updates and feeds the deferred fill's indirect.</summary>
    /// <remarks>
    /// The stable counterpart to the screen gather. A grid of world-anchored probes traces the surface
    /// cache every frame into the same L1-SH layout the baked probe field uses, so the deferred pass samples
    /// it unchanged -- but the radiance is live, not baked, so the sun and dynamic surfaces still feed it,
    /// and because a probe lives in a world cell rather than on the moving screen it does not shimmer under
    /// motion the way the gather does. It supersedes the baked field when on, and needs the surface cache
    /// (<c>r_cards</c>) and the distance field (<c>r_sdf</c>) to trace against. On at every preset above
    /// vanilla, each of which runs the surface cache and the distance field it needs. It is the shippable
    /// form of the real-time indirect the screen gather could not steady.
    /// </remarks>
    public static readonly NumericCVar<int> RadianceCache = Presets.Define(
        CVar.Int(
            "r_radiance_cache",
            0,
            0,
            1,
            CVarOptions.Archive,
            "Update the world-space radiance cache and feed it to the indirect fill. Needs r_cards and r_sdf."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Calibration on the radiance cache's stored bounce, where 1 is the raw traced radiance.</summary>
    /// <remarks>
    /// The cache holds actual card radiance, and how strongly it is fed into the deferred fill is what turns
    /// a sunlit floor into a room lit by the light it bounces up its walls. Half was far too timid: it left a
    /// window room showing only the sun's own patches on the floor with no bounce climbing the walls, the
    /// single-bounce indirect starved to almost nothing. Lifted so the bounce actually carries the room --
    /// validated on mission 1's office, where the sun through the window now fills the whole room, and on the
    /// outdoor spawn, which does not blow out. The sealed-room leak this would
    /// also amplify is cut at its source instead (see the sky-leak reject in the cache trace), not by keeping
    /// the whole bounce dim.
    /// </remarks>
    public static readonly NumericCVar<float> RadianceCacheScale = Presets.Define(
        CVar.Float(
            "r_radiance_cache_scale",
            3f,
            0f,
            8f,
            CVarOptions.Archive,
            "Calibration on the radiance cache's bounce; 1 is the raw traced radiance."),
        vanilla: "3",
        enhanced: "3",
        high: "3",
        ultra: "3");

    /// <summary>Whether the screen probe gather traces on-screen geometry from the frame before the cache.</summary>
    /// <remarks>
    /// On by default, but only ever engaged while <see cref="ScreenGather"/> is up: each gather ray first
    /// marches the depth buffer and, on an on-screen hit, reads the previous frame's lit colour there --
    /// on-screen bounce at full frame detail rather than the surface cache's coarse card -- falling
    /// through to the cache only when the ray leaves the screen or finds nothing. A knob to turn off for
    /// comparison; it does nothing when the gather itself is off.
    /// </remarks>
    public static readonly BoolCVar ScreenGatherTrace = Presets.Define(
        CVar.Bool(
            "r_screen_gi_trace",
            true,
            CVarOptions.Archive,
            "Trace on-screen geometry from the frame before falling back to the surface cache."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How many frames apart the screen probe gather re-traces. One is every frame.</summary>
    /// <remarks>
    /// <para>
    /// The gather is the single most expensive pass in a real-time-GI frame -- it traces sixteen rays from
    /// each probe of a grid covering the screen, and at 1440p that is nearly a million rays. This runs it
    /// every N frames instead of every one, carrying the atlas forward in between.
    /// </para>
    /// <para>
    /// What makes that sound is that a probe's value is a running average over <see cref="ScreenGather"/>'s
    /// own accumulation -- dozens of frames -- so a probe is already a slow-moving estimate and re-tracing
    /// it twice as often does not make it twice as right. What genuinely has to happen every frame is the
    /// FULL-RESOLUTION RESOLVE, which reprojects the result against each pixel's own world position: that
    /// is what pins the indirect to the world while the camera moves, and it keeps running at frame rate
    /// here. So the indirect still tracks the view every frame; only the rate at which new light is traced
    /// into it drops. The cost is how quickly newly revealed geometry fills in -- a frame or two longer.
    /// </para>
    /// <para>
    /// Reducing the RAY COUNT instead was measured and is a worse trade: it makes every probe's estimate
    /// noisier, which the spatial filter then spreads, where this leaves each estimate exactly as good and
    /// only arrives less often.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> ScreenGatherCadence = Presets.Define(
        CVar.Int(
            "r_screen_gi_cadence",
            1,
            1,
            4,
            CVarOptions.Archive,
            "Frames between screen probe gather re-traces. 1 is every frame; higher is cheaper, fills in slower."),
        vanilla: "1",
        enhanced: "2",
        high: "1",

        // ONE at high, where the accuracy is the point; TWO at ultra, which since the far-refresh idle is the
        // tier where the gather is the largest single pass (2.7 of ~15.4 ms at 1440p) and halving it measured
        // about a millisecond. It is not free: the probe grid is anchored to the SCREEN, so an atlas
        // carried over from the previous frame is misaligned with the depth buffer the resolve upsamples it
        // against, and the resolve accumulates that misalignment into its per-pixel history every frame.
        // Measured against the same three still-camera scenes the corner fix was validated on, it moved the
        // whole image by a mean of 1.7/255 with visible structure across every surface -- a systematic bias
        // rather than noise, though still inside this project's own 2/255 acceptance.
        //
        // Enhanced and ultra take that trade; high keeps every-frame tracing. Set it to 1 by hand for the
        // unbiased indirect at ultra.
        ultra: "2");

    /// <summary>How far the screen probe gather's spatial denoise blur reaches, in probes. Zero is off.</summary>
    /// <remarks>
    /// The spatial half of the gather's denoise, paired with its temporal accumulation. The per-frame
    /// estimate is noisy and the temporal pass resets along depth edges, so the probes freshest near a
    /// wall shimmer; blending each probe with its neighbours, weighted so light does not cross an edge,
    /// smooths that. One probe each way is usually enough; higher is smoother but softer. Only ever runs
    /// while <see cref="ScreenGather"/> is up.
    /// </remarks>
    public static readonly NumericCVar<int> ScreenGatherFilter = Presets.Define(
        CVar.Int(
            "r_screen_gi_filter",
            2,
            0,
            4,
            CVarOptions.Archive,
            "Radius, in probes, of the screen probe gather's spatial denoise. 0 is off."),
        vanilla: "2",
        enhanced: "1",
        high: "2",
        ultra: "2");

    /// <summary>The escape fraction below which a radiance-cache probe's sky is treated as a wall leak.</summary>
    /// <remarks>
    /// A cache probe traces the whole sphere; the fraction of its rays reaching open sky says whether the
    /// sky it sees is a real opening or a leak through a wall the coarse distance field cannot seal. Above
    /// this fraction the sky is kept in full, below a third of it dropped entirely, so a sealed room -- whose
    /// probes escape only on the few rays that slip through a wall -- falls dark rather than borrowing the
    /// day outside, while a window room, whose probes escape freely, keeps its sky. A window room is lit
    /// mostly by its sunlit floor bouncing regardless, so this can be firm without darkening it.
    /// </remarks>
    public static readonly NumericCVar<float> GlobalIlluminationLeakReject = Presets.Define(
        CVar.Float(
            "r_gi_leak_reject",
            0.3f,
            0f,
            1f,
            CVarOptions.Archive,
            "Escape fraction under which a cache probe's sky is treated as a wall leak and dropped."),
        vanilla: "0.3",
        enhanced: "0",
        high: "0",
        ultra: "0");

    /// <summary>Diagnostic: which one source of the screen GI gather's light to keep.</summary>
    /// <remarks>
    /// The gather lights each probe from three places: screen-traced hits (the last frame's image), hits on
    /// the surface cache, and the sky where a ray escapes. With r_debug_view ssgi this shows one of them
    /// alone, so a GI artefact can be pinned on the source that carries it.
    /// </remarks>
    public static readonly NumericCVar<int> GiSource = CVar.Int(
        "r_gi_source",
        0,
        0,
        4,
        CVarOptions.Developer,
        "Diagnostic: keep one source of the screen GI: 0 all, 1 screen hits, 2 surface-cache hits, 3 sky, 4 the surface cache itself.");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
