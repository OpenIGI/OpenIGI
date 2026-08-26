using OpenIGI.Core.Configuration;
using OpenIGI.Rendering.Presets;

namespace OpenIGI.Rendering;

/// <summary>Controls how much runtime diagnostic output is produced.</summary>
public enum LogVerbosity
{
    /// <summary>Write no runtime log messages.</summary>
    Off,

    /// <summary>Write warnings and failures, but omit normal progress and gameplay diagnostics.</summary>
    Minimal,

    /// <summary>Write normal progress messages as well as warnings and failures.</summary>
    Verbose,
}

/// <summary>
/// The renderer's console variables.
/// </summary>
/// <remarks>
/// <para>
/// Declared here, next to the renderer seam, rather than in a central settings type. The console, the
/// settings file and the options menu all reach these through
/// <see cref="CVarRegistry.Default"/>, so there is one value and three views onto it.
/// </para>
/// <para>
/// Each remaster stage adds its own variables to this file and, where the value differs by quality
/// tier, registers them with <see cref="Presets.GraphicsPresetRegistry"/> in the same place.
/// </para>
/// </remarks>
[CVarContainer]
public static class RenderCVars
{
    /// <summary>Runtime logging level. Minimal is intentionally the default for gameplay performance.</summary>
    public static readonly EnumCVar<OpenIGI.Rendering.LogVerbosity> LogVerbosity = CVar.Choice(
        "con_log_verbosity",
        OpenIGI.Rendering.LogVerbosity.Minimal,
        CVarOptions.Archive,
        "Runtime log output: off, minimal (warnings/errors), or verbose (normal diagnostics).");

    /// <summary>Whether presentation waits for the vertical blank.</summary>
    public static readonly BoolCVar VSync = CVar.Bool(
        "r_vsync",
        true,
        CVarOptions.Archive,
        "Wait for the vertical blank when presenting. Off uncaps the frame rate and adds tearing.");

    /// <summary>How bright a gun's muzzle flame draws, as a fraction of the original's.</summary>
    /// <remarks>
    /// <para>
    /// The flame is an additive glow whose brightness rides its vertex colours, so this scales those
    /// at upload. Zero removes the flash entirely; one is the retail fireball, which vanilla pins so
    /// the original's picture stays exact.
    /// </para>
    /// <para>
    /// The remastered presets sit at 0.45, measured against the tactical-ballistics tracers: the
    /// retail flame is bright enough to swallow the first metres of a tracer, and a subtler flash is
    /// what every modern shooter draws anyway.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> MuzzleFlash = Presets.Define(
        CVar.Float(
            "r_muzzle_flash",
            1f,
            0f,
            1f,
            CVarOptions.Archive,
            "Muzzle flash brightness. 1 is the original's, 0 removes it."),
        vanilla: "1",
        enhanced: "0.45",
        high: "0.45",
        ultra: "0.45");

    /// <summary>How rich the particle burst where a round lands is, or 0 for none.</summary>
    /// <remarks>
    /// A remaster addition: sparks and dust composed from the material table's own row for the
    /// struck surface — metal showers sparks, dirt breathes a slow brown plume, a body a brief red
    /// mist. One is the authored look, two doubles the particle counts, and vanilla pins zero
    /// because the original drew nothing of the kind.
    /// </remarks>
    public static readonly NumericCVar<float> ImpactParticles = Presets.Define(
        CVar.Float(
            "r_impact_particles",
            1f,
            0f,
            2f,
            CVarOptions.Archive,
            "Particle bursts where rounds land: sparks, dust and debris by surface. 0 turns them off."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the marks rounds leave on the world are drawn.</summary>
    /// <remarks>
    /// Presentation only — the marks are still recorded, so turning this back on shows them. Exists
    /// as much for diagnosis as for taste: decals are one of a handful of world-space quad renderers,
    /// and being able to rule them in or out from the console in seconds is worth a variable.
    /// </remarks>
    public static readonly BoolCVar BulletMarks = Presets.Define(
        CVar.Bool(
            "r_bulletmarks",
            true,
            CVarOptions.Archive,
            "Draw the marks rounds leave on surfaces."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the authored level weather is rendered.</summary>
    public static readonly BoolCVar WeatherEnabled = CVar.Bool(
        "r_weather_enabled",
        true,
        CVarOptions.Archive,
        "Enable the level's rain or snow particle weather.");

    /// <summary>Which particle weather style is rendered when weather is enabled.</summary>
    public static readonly EnumCVar<WeatherKind> WeatherStyle = CVar.Choice(
        "r_weather_kind",
        global::OpenIGI.Rendering.WeatherKind.Rain,
        CVarOptions.Archive,
        "Choose rain or snow for the active level weather descriptor.");

    /// <summary>Multiplier applied to the authored weather particle fall speed.</summary>
    public static readonly NumericCVar<float> WeatherSpeed = CVar.Float(
        "r_weather_speed",
        1f,
        0f,
        2f,
        CVarOptions.Archive,
        "Weather particle speed multiplier: 0 stops falling, 1 is the tuned speed, 2 is twice as fast.");

    /// <summary>Whether the presentation interpolates between simulation ticks.</summary>
    /// <remarks>
    /// <para>
    /// The simulation is fixed at 30 Hz and stays there — that is the determinism contract. What this
    /// changes is only what is <em>drawn</em>: on, each frame renders bodies, poses and the player's
    /// own eye blended between the last two tick states, so an uncapped frame rate looks uncapped
    /// instead of a 30 Hz slideshow with a smooth mouse. AI, ballistics and every other consumer keep
    /// reading tick states, so nothing behaves differently.
    /// </para>
    /// <para>
    /// Off under vanilla by definition: the original presents exactly one tick's state per frame, and
    /// the reference captures must keep doing so.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar SmoothPresentation = Presets.Define(
        CVar.Bool(
            "r_smooth",
            true,
            CVarOptions.Archive,
            "Draw between simulation ticks so movement is as smooth as the frame rate. Sim stays 30 Hz."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How the picture is scaled relative to the window before being presented.</summary>
    /// <remarks>
    /// Below one this is supersampling in reverse — the scene is rendered smaller and upscaled, which
    /// is the cheapest way to buy back frame time. Above one it is supersampling proper. The temporal
    /// upscaler added in stage R5 is what makes values below one look acceptable.
    /// </remarks>
    public static readonly NumericCVar<float> RenderScale = CVar.Float(
        "r_scale",
        1.0f,
        0.5f,
        2.0f,
        CVarOptions.Archive,
        "Render resolution as a fraction of the window: below 1 is faster, above 1 is supersampled.");

    /// <summary>Whether the scene is rendered offscreen instead of straight to the back buffer.</summary>
    /// <remarks>
    /// The switch between the original's path and the modern one. Off, the frame is drawn and
    /// presented exactly as the game shipped, and none of the remaster track's effects can run,
    /// because every one of them needs the frame and its depth to still exist after being drawn.
    /// </remarks>
    public static readonly BoolCVar OffscreenScene = Presets.Define(
        CVar.Bool(
            "r_hdr",
            true,
            CVarOptions.Archive,
            "Render the scene offscreen so it can be tone mapped and post-processed. Off is the original's own path."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How out-of-range light is brought back into what a display can show.</summary>
    public static readonly EnumCVar<ToneMapOperator> ToneMap = Presets.Define(
        CVar.Choice(
            "r_tonemap",
            ToneMapOperator.Neutral,
            CVarOptions.Archive,
            "Tone map: none reproduces the original exactly, neutral stays out of the way, aces and agx are filmic."),
        vanilla: "none",

        // ACES filmic across the modern presets. Neutral was fine while the world had no highlights to tame,
        // but with auto-exposure on and a window several stops brighter than the room it stands in, there is
        // now a real highlight range: ACES' shoulder rolls the blown window off with its colour intact rather
        // than clipping it to flat white, and holds more midtone contrast than AgX over the range this game
        // occupies. Vanilla stays untouched -- none reproduces the original exactly.
        enhanced: "aces",
        high: "aces",
        ultra: "aces");

    /// <summary>The linear value that maps to display white.</summary>
    /// <remarks>
    /// How much headroom above white the tone map reserves. It must match what the scene produces:
    /// this world is LDR textures multiplied by LDR lightmaps, so almost nothing exceeds one, and the
    /// old fixed value of 4 spent the curve's whole range compressing a span that was not there. One
    /// disables the roll-off completely.
    /// </remarks>
    public static readonly NumericCVar<float> WhitePoint = Presets.Define(
        CVar.Float(
            "r_white_point",
            1.1f,
            1f,
            8f,
            CVarOptions.Archive,
            "Linear value that maps to display white. Lower is more contrast; 1 disables the roll-off."),
        vanilla: "1.1",
        enhanced: "1.1",
        high: "1.1",
        ultra: "3");

    /// <summary>A linear multiplier applied to the scene before tone mapping.</summary>
    /// <remarks>
    /// With <see cref="AutoExposure"/> on this becomes a bias on the adapted exposure rather than the
    /// whole of it: the eye adapts to the scene's own brightness and this nudges the result up or down.
    /// </remarks>
    public static readonly NumericCVar<float> Exposure = Presets.Define(
        CVar.Float(
            "r_exposure",
            1.0f,
            0.05f,
            16.0f,
            CVarOptions.Archive,
            "Scene brightness before tone mapping. 1 leaves the level's own lighting alone."),

        // Vanilla never tone maps, so its value is ignored. The others are calibrated so the scene reads
        // right at a FIXED exposure -- no auto-exposure crutch. All three tone-mapped presets now light
        // from the artists' baked lightmaps (ultra reverted from the dynamic model -- see r_lighting), so
        // their exposures sit close together: enhanced is the baseline, high lifts a touch to offset the
        // contact shadows and heavier AO it adds, and ultra lifts a touch more to offset ACES pulling the
        // midtones down where the others use neutral. The tone map rolls off the sky, so lifting interiors
        // this far does not blow the exterior -- measured across the outdoor, window and windowless views.
        vanilla: "1",
        enhanced: "1.4",
        high: "1.6",

        // Recalibrated once the metering and the curve were the reference's. The old 0.6 DARKENED the
        // adapted exposure, a crutch from when a whole-frame mean meter -- dragged up by any window -- was
        // over-reporting the scene's brightness, so it had to be pulled back down. The meter is now a
        // histogram percentile band that leaves the window out, local exposure lifts the shadows, and the
        // warm ambient/sky tints that muddied the frame are gone; the scene reports its real, lower
        // brightness, so the bias goes the other way and OPENS it up to the reference's bright, airy
        // interior. Auto-exposure still adapts around this; it is the bias on that, near the +0.4 EV the
        // reference biases a daylight interior by. Measured on the office (bright, filled, window held under
        // the shoulder, its channel means on the reference's) and the outdoor spawn (balanced, not blown).
        // Now 1.414: exactly the reference's auto-exposure bias of half a stop (2^0.5), applied the same
        // way -- as compensation multiplied onto the adapted exposure -- so the two meters agree.
        ultra: "1.414");

    /// <summary>How much of the surface cache's bounced radiance a traced gather ray keeps.</summary>
    /// <remarks>
    /// One is the physical answer: the cache holds real outgoing radiance and a ray that hits a card
    /// should keep all of it. The reference render this project matches bounces far less than that —
    /// its surface cache reports no coverage over most of this level's imported geometry, so its rays
    /// hit surfaces that have no cached lighting to return and its interiors go near-black beyond the
    /// direct sun. This scales OUR cache's answer down toward that look without touching the sky, which
    /// the reference does resolve. Applies to the screen gather and the radiance cache alike.
    /// </remarks>
    public static readonly NumericCVar<float> GiBounce = Presets.Define(
        CVar.Float(
            "r_gi_bounce",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "Traced bounce strength off the surface cache. 1 is physical; lower matches the reference's sparser bounce."),
        vanilla: "1",
        enhanced: "1",
        high: "1",

        // Matched against the reference office shot: 0.15 lands the walls and ceiling on its values with
        // the window, the sun pools and the exterior already aligned. Raise toward one for the physical
        // answer — the room fills with real multi-bounce — at the cost of the reference's darker mood.
        ultra: "0.15");

    /// <summary>Whether tracing uses the hardware ray query rather than the software distance field.</summary>
    /// <remarks>
    /// The switch between the two GI modes, live: on, every trace that can reach the acceleration
    /// structure does — the screen gather, the radiance cache, the surface-cache relight and the sun's
    /// shadow — and off, the same shaders march the signed distance field and the sun falls back to the
    /// cascaded map. It only means anything on a backend that can trace at all; elsewhere the software
    /// path is the only path and the flag is ignored. Bound to a key by default so the two can be
    /// compared in place.
    /// </remarks>
    public static readonly BoolCVar HardwareRayTracing = Presets.Define(
        CVar.Bool(
            "r_rt",
            true,
            CVarOptions.Archive,
            "Trace with the hardware ray query (1) or the software distance field (0). Toggle live to compare."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How bright a single screen-traced ray may come back, before it feeds the next frame.</summary>
    /// <remarks>
    /// The screen trace resolves a hit from the previous frame's lit scene, and this frame's indirect is
    /// built out of what it returns — which then lights the scene the next trace reads. Unbounded, that
    /// loop does not settle: past a certain gather strength each pass is brighter than the last. The
    /// reference bounds the same read for the same reason, and calls it the maximum ray intensity.
    /// </remarks>
    public static readonly NumericCVar<float> ScreenGatherMaxIntensity = Presets.Define(
        CVar.Float(
            "r_screen_gi_max",
            10f,
            0.1f,
            1000f,
            CVarOptions.Archive,
            "Ceiling on a single screen-traced ray's radiance; stops the gather feeding itself."),
        vanilla: "10",
        enhanced: "10",
        high: "10",
        ultra: "50.1");

    /// <summary>How strongly the screen probe gather's indirect light fills the deferred lighting.</summary>
    /// <remarks>
    /// Zero is off, which is every preset below ultra: above zero, the gather traces the surface cache per
    /// screen probe into an atlas and the deferred lighting reads it as its diffuse fill over the far-field
    /// floor -- the stage where the traced global illumination reaches the screen. Not a name
    /// clash with <see cref="GlobalIllumination"/> (<c>r_gi</c>), which is the baked probe field; this is
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
        enhanced: "0",
        high: "0",

        // On at ultra. The screen probe gather is a real-time indirect -- it traces the surface cache along
        // the view so the bounce responds to the frame -- and it is what carries the warm bounced light the
        // interior reads as: the world-space radiance cache alone, sampled straight, comes back markedly
        // cooler than the reference (its far-field trace misses the near, sun-warmed bounce a screen ray
        // catches), so the gather is not optional for the look, it is the look. Two things make it shippable
        // where the raw screen estimate was not, both added with it. Its full-resolution temporal resolve
        // now clamps each reprojected history into the current frame's own neighbourhood mean and spread --
        // the reference's anti-shimmer -- so a stale or noisy history can no longer slide across a wall as
        // the low-frequency shimmer that once shelved it. And the deferred fill floors the gather to the
        // BRIGHTER of it and the far field: the radiance cache runs at this same tier, so a surface the
        // gather has not yet resolved -- a freshly disoccluded pixel -- falls to the cache's stable answer
        // rather than to black, which is the dropout the screen estimate had with no world-space field
        // behind it. One, because the gather now returns physically-normalised irradiance -- the
        // pi-over-N estimator with the sky integrated ray by ray -- and the deferred fill takes it
        // through the Lambert response. Anything other than one is an artistic bias on real light
        // transport, not a calibration.
        ultra: "1");

    /// <summary>Whether the world-space radiance cache updates and feeds the deferred fill's indirect.</summary>
    /// <remarks>
    /// The stable counterpart to the screen gather. A grid of world-anchored probes traces the surface
    /// cache every frame into the same L1-SH layout the baked probe field uses, so the deferred pass samples
    /// it unchanged -- but the radiance is live, not baked, so the sun and dynamic surfaces still feed it,
    /// and because a probe lives in a world cell rather than on the moving screen it does not shimmer under
    /// motion the way the gather does. It supersedes the baked field when on, and needs the surface cache
    /// (<c>r_cards</c>) and the distance field (<c>r_sdf</c>) to trace against. On at ultra, which already
    /// runs the surface cache and the distance field it needs; off below, where the baked field carries the
    /// indirect. It is the shippable form of the real-time indirect the screen gather could not steady.
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
        enhanced: "0",
        high: "0",
        ultra: "1");

    /// <summary>Whether the world pass culls back faces, now that the mesh builder repairs triangle winding.</summary>
    /// <remarks>
    /// Culling was off because the port's flattened index order did not wind uniformly, so removing either
    /// facing dropped real walls. The mesh builder now rewinds each triangle to agree with its vertices' own
    /// outward normals, which gives back-face culling a consistent facing to remove -- and that is what stops
    /// a thin object's two coincident faces from fighting over a pixel (the glittering z-fight on thin
    /// geometry), while roughly halving the indoor fill. Off leaves both facings drawn, the old behaviour.
    /// A true single-quad, two-sided surface (a chain-link fence read as one plane) loses its far side under
    /// this; those are rare enough that the trade is worth the thin-geometry fix and the fill it saves.
    /// </remarks>
    public static readonly NumericCVar<int> WorldCull = Presets.Define(
        CVar.Int(
            "r_world_cull",
            0,
            0,
            1,
            CVarOptions.Archive,
            "Cull back faces in the world pass. Fixes thin-geometry z-fighting; needs the winding repair."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // On at ultra, where the winding repair makes it safe: validated on mission 1 (the office window's
        // thin mullions stop z-fighting and read clean, and no wall is lost indoors or out). The lower
        // presets stay off until the repair is validated across the other missions.
        ultra: "1");

    /// <summary>Calibration on the radiance cache's stored bounce, where 1 is the raw traced radiance.</summary>
    /// <remarks>
    /// The cache holds actual card radiance, and how strongly it is fed into the deferred fill is what turns
    /// a sunlit floor into a room lit by the light it bounces up its walls. Half was far too timid: it left a
    /// window room showing only the sun's own patches on the floor with no bounce climbing the walls, the
    /// single-bounce indirect starved to almost nothing. Lifted so the bounce actually carries the room --
    /// validated on mission 1's office, where the sun through the window now fills the whole room the way the
    /// reference's does, and on the outdoor spawn, which does not blow out. The sealed-room leak this would
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

    /// <summary>How many frames the surface-cache relight spreads its tiles across.</summary>
    /// <remarks>
    /// The real-time GI relight is world-space -- its cost is the same at any output resolution, so it is
    /// the biggest single item in the budget on a high-resolution frame. It traces one slice of the cache a
    /// frame and walks the cursor over this many frames; a static level under a fixed sun re-traces a tile to
    /// the answer it already holds, so a wider spread is nearly free in a converged still and only slows how
    /// fast a newly revealed surface fills its multi-bounce light. Raise it to trade that fill-in latency for
    /// per-frame GPU time (roughly the whole GI stage divides by this), lower it for the snappiest response.
    /// </remarks>
    public static readonly NumericCVar<int> GiAmortize = Presets.Define(
        CVar.Int(
            "r_gi_amortize",
            12,
            2,
            48,
            CVarOptions.Archive,
            "Frames the surface-cache relight spreads its tiles across; higher is cheaper per frame, lags more."),
        vanilla: "12",
        enhanced: "12",
        high: "12",
        ultra: "12");

    /// <summary>The hour of day the interactive sun starts a level at, 0 to 24. Noon is the authored sun.</summary>
    /// <remarks>
    /// The day/night cycle sweeps the level's one authored sun across the sky by a time of day the player
    /// scrubs live with the bracket keys; this is where that time starts each level. Twelve reproduces the
    /// authored noon sun exactly, which is why it is the default -- a level opens looking as it always did.
    /// Set it to open on a sunrise, a sunset or a night instead. It only seeds the starting hour; the keys
    /// move it from there.
    /// </remarks>
    public static readonly NumericCVar<float> TimeOfDay = CVar.Float(
        "r_time_of_day",
        12f,
        0f,
        24f,
        CVarOptions.Archive,
        "Hour of day the interactive sun starts a level at, 0-24. 12 is the authored noon sun.");

    /// <summary>How strongly the level's inferred lamp fixtures light the scene as real point lights.</summary>
    /// <remarks>
    /// The screen gather only carries light that already exists in the frame -- daylight through a window,
    /// a sunlit wall -- so a windowless interior it cannot help: there is no source to gather. This turns
    /// the lamp fixtures the emissive survey proposes (see the game layer's light-fixture proposer) into
    /// direct punctual lights, which is the honest way to light such a room -- a real source with a real
    /// falloff, not exposure lifting a black frame into grey. Scales the whole set; the fixtures' relative
    /// intensities are the survey's, and this is the one number tuned by eye. Off below ultra, where the
    /// bakes still carry the interiors.
    /// </remarks>
    public static readonly NumericCVar<float> LocalLightScale = Presets.Define(
        CVar.Float(
            "r_local_light_scale",
            0f,
            0f,
            64f,
            CVarOptions.Archive,
            "Strength of the level's inferred lamp fixtures as real point lights. 0 is off."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "6");

    /// <summary>How bright the sky is as a source of indirect light, where 1 is the level's own ambient.</summary>
    /// <remarks>
    /// The GI reads the sky as the colour a ray that escapes the geometry returns -- the skylight that
    /// fills every outdoor surface and reaches in through a window. The level's <c>Dirlight</c> carries
    /// only a dim ambient triple (its "Back Color", around a quarter), authored as a subtle fill on top of
    /// the flat ambient rather than as the sky's whole radiance; read literally as the sky it leaves the
    /// open exterior reading like dusk. This scales it up to a daytime value. It touches only what looks at
    /// the sky, so a sealed room is unaffected while the courtyard and the window-lit wall lift -- and it
    /// only ever reaches a preset that runs the gather or the probe field, so vanilla and the baked path
    /// are untouched.
    /// </remarks>
    public static readonly NumericCVar<float> SkyLight = Presets.Define(
        CVar.Float(
            "r_sky_light",

            // The reference scene's SkyLight multiplies its captured sky by three; matching it here keeps
            // the two renders' window light on the same scale.
            3f,
            0f,
            16f,
            CVarOptions.Archive,
            "Skylight brightness the GI fill sees; 1 is the level's own ambient. Lifts outdoor and window-lit surfaces."),
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
        enhanced: "2",
        high: "2",
        ultra: "2");

    /// <summary>Whether the exposure adapts to the scene's own brightness over time.</summary>
    /// <remarks>
    /// Off by default, and only ever engages where a tone map is on -- the vanilla copy path returns
    /// before exposure is applied at all, so a traced preset can adapt while vanilla stays byte-identical.
    /// A traced scene without eye adaptation reads worse than one lit the original's way, which is why the
    /// plan calls it non-optional there; it is opt-in here until a preset turns it on.
    /// </remarks>
    public static readonly BoolCVar AutoExposure = Presets.Define(
        CVar.Bool(
            "r_autoexposure",
            false,
            CVarOptions.Archive,
            "Adapt exposure to the scene's average brightness over time. Only where a tone map is on."),
        vanilla: "0",

        // ON for every modern preset. It was turned off in the fixed-exposure era on the theory that a
        // calibrated exposure lit interiors and exteriors together -- but a fixed exposure tuned for a sunlit
        // exterior crushes an interior to black, which is exactly what a windowed room looked like: dark and
        // flat. Adapting the exposure to what the camera is actually looking at is what every modern engine
        // does, and it is what lets a dim room read bright next to a blown-out window while the tone map's
        // shoulder rolls the window off rather than clipping it. r_exposure is a bias on the adapted value
        // here, no longer the whole of it. Vanilla keeps the original's fixed brightness.
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How many samples per pixel the scene is rasterised with.</summary>
    /// <remarks>
    /// Resolved down before anything samples the target, because the effects that follow read depth
    /// and a multisampled depth buffer is not something a shader can read usefully.
    /// </remarks>
    public static readonly NumericCVar<int> MultiSampling = Presets.Define(
        CVar.Int(
            "r_msaa",
            4,
            0,
            8,
            CVarOptions.Archive,
            "Multisample count for the scene: 0 or 1 is off. Only applies when r_hdr is on."),
        vanilla: "0",
        enhanced: "2",
        high: "4",
        ultra: "8");

    /// <summary>How far the terrain keeps subdividing before it settles for a coarser mesh.</summary>
    /// <remarks>
    /// <para>
    /// Not an optimisation that can be switched off. The terrain <em>is</em> an octree, so there is no
    /// underlying full-detail mesh to stop simplifying — this is the depth the descent runs to, and
    /// removing the limit would mean subdividing a cube 262 km on a side.
    /// </para>
    /// <para>
    /// Retail runs 96, 129 or 163 for low, medium and high, and 163 is the shipped default
    /// (<c>sub_406390</c>). The remaster's tiers go well past it, which is what actually removes the
    /// popping as ground resolves ahead of the player.
    /// </para>
    /// <para>
    /// <b>Zero means "as retail derives it"</b>, and is not the same as writing 163: the renderer
    /// takes 163 as a <em>base</em> and divides it by the square root of the horizontal half-angle
    /// tangent, so at anything but a 4:3 window the two differ. Vanilla therefore asks for zero, not
    /// for a number that only looks like the default.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> TerrainDetail = Presets.Define(
        CVar.Int(
            "r_terrain_detail",
            0,
            0,
            600,
            CVarOptions.Archive,
            "How far terrain keeps subdividing; 0 derives it as retail does. Higher resolves ground further out."),
        vanilla: "0",
        enhanced: "220",
        high: "320",
        ultra: "450");

    /// <summary>Whether terrain batch pages use asynchronous visibility queries.</summary>
    /// <remarks>
    /// The query result is consumed only after OpenGL reports it ready, so this never stalls the CPU.
    /// Keeping this as a CVar also makes the query cost directly A/B testable on a given mission.
    /// </remarks>
    public static readonly BoolCVar TerrainOcclusion = Presets.Define(
        CVar.Bool(
            "r_terrain_occlusion",
            false,
            CVarOptions.Archive,
            "Cull fully hidden terrain batch pages with asynchronous GPU visibility queries. Opt-in because sparse scenes can cost more to query than they save."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "1");

    /// <summary>Whether placed world objects use asynchronous visibility queries in the geometry pass.</summary>
    /// <remarks>
    /// The frustum keeps most of a level's objects while the camera stands indoors — a wall hides
    /// them, a frustum does not. On by default because interiors are where the geometry pass was
    /// measured most over budget; a CVar so the gain is A/B testable per mission like the terrain's.
    /// </remarks>
    public static readonly BoolCVar WorldOcclusion = Presets.Define(
        CVar.Bool(
            "r_world_occlusion",
            true,
            CVarOptions.Archive,
            "Cull placed objects hidden behind walls and hills with asynchronous GPU visibility queries."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether surfaces are recorded into a G-buffer and lit afterwards.</summary>
    /// <remarks>
    /// Required by everything from stage R3 on. Kept separate from <see cref="OffscreenScene"/> so the
    /// offscreen pipeline can be exercised on its own — which is what the round-trip check in
    /// <c>docs/remaster/reference/README.md</c> does.
    /// </remarks>
    public static readonly BoolCVar Deferred = Presets.Define(
        CVar.Bool(
            "r_deferred",
            true,
            CVarOptions.Archive,
            "Record surfaces into a G-buffer and light them afterwards. Needed for shadows, AO and reflections."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Where surfaces take their light from.</summary>
    /// <remarks>
    /// Stays on <c>baked</c> for every preset until the sun of stage R3 and the probe irradiance of R7
    /// exist. Promoting it before then would trade hand-authored lighting for a flat ambient term,
    /// which is not an improvement in anybody's judgement.
    /// </remarks>
    public static readonly EnumCVar<LightingModel> Lighting = Presets.Define(
        CVar.Choice(
            "r_lighting",
            LightingModel.Baked,
            CVarOptions.Archive,
            "Light source: baked reproduces the original, hybrid adds the sun, dynamic ignores the bakes."),
        vanilla: "baked",
        enhanced: "baked",
        high: "baked",

        // Ultra is the real-time preset. It lights from the screen probe gather (r_screen_gi), FLOORED
        // against the baked probe volume (r_gi, on at ultra for exactly this) so a surface the screen field
        // cannot resolve -- a thin edge whose probes all land on the sky, an off-screen wall -- is lit by
        // the coarse visibility-aware field rather than dropped to a flat ambient or to black. That floor is
        // the probe-irradiance field this used to wait for: with it the terrain's per-triangle facets and
        // the unlit interiors dynamic once fell into are the field's to carry, and the lamps
        // (r_local_light_scale) light the windowless rooms the screen gather has no source for. The cheaper
        // presets keep the artists' bakes.
        ultra: "dynamic");

    /// <summary>The side length in texels of each shadow cascade, or 0 for no shadows.</summary>
    /// <remarks>
    /// The cost is per cascade and quadratic: four cascades at 4096 is 268 MB of depth, which is more
    /// video memory than the entire rest of this game uses. Ultra buys its quality with a fourth
    /// cascade rather than with a larger one — more cascades put the texels where the player is
    /// looking, while a larger map spends them evenly over ground they cannot make out.
    /// </remarks>
    public static readonly NumericCVar<int> Shadows = Presets.Define(
        CVar.Int(
            "r_shadows",
            2048,
            0,
            4096,
            CVarOptions.Archive,
            "Sun shadow map resolution per cascade; 0 turns shadows off. Cost is per cascade and quadratic."),
        vanilla: "0",
        enhanced: "1024",
        high: "2048",

        // 4096 at ultra. The window mullions cast a fine grid of shadow across the floor, and at 2048 -- with
        // the cascade's texels spread over a room's worth of world -- each bar's shadow fell across only a few
        // texels and read as a soft blur. Doubled, the grid comes in crisp, the clean sunlit pattern the
        // reference draws rather than a smear.
        ultra: "4096");

    /// <summary>How many cascades the shadow distance is split across.</summary>
    public static readonly NumericCVar<int> ShadowCascades = Presets.Define(
        CVar.Int(
            "r_shadow_cascades",
            3,
            1,
            4,
            CVarOptions.Archive,
            "How many shadow cascades the view distance is split across."),
        vanilla: "1",
        enhanced: "2",
        high: "3",
        ultra: "4");

    /// <summary>How far from the camera, in metres, shadows are drawn.</summary>
    /// <remarks>
    /// Further is not better past a point: the same texels get spread over more world, so distant
    /// shadows arrive at the cost of the ones under the player's feet.
    /// </remarks>
    public static readonly NumericCVar<float> ShadowDistance = Presets.Define(
        CVar.Float(
            "r_shadow_distance",
            220f,
            20f,
            1000f,
            CVarOptions.Archive,
            "How far from the camera sun shadows are drawn, in metres."),
        vanilla: "220",
        enhanced: "150",
        high: "220",

        // Pulled in from 320 to concentrate the cascades' resolution on the near field, where the window's
        // shadow grid on the floor is -- the sharpness there is worth more than shadows out past the haze,
        // which the fog swallows anyway. The near building and fence shadows outdoors still read.
        ultra: "150");

    /// <summary>How far a screen-space contact ray reaches, in metres. Zero disables it.</summary>
    /// <remarks>
    /// A shadow map has one texel per several centimetres at best, so it cannot resolve a weapon
    /// against the body carrying it or a crate against the floor it stands on. Contact is where its
    /// resolution runs out and where the eye looks first.
    /// </remarks>
    public static readonly NumericCVar<float> ContactShadows = Presets.Define(
        CVar.Float(
            "r_contact_shadows",
            0.35f,
            0f,
            2f,
            CVarOptions.Archive,
            "Reach in metres of the screen-space contact shadow ray; 0 turns it off."),
        vanilla: "0",
        enhanced: "0",
        high: "0.35",
        ultra: "0.5");

    /// <summary>The shadow filter's radius in texels.</summary>
    public static readonly NumericCVar<float> ShadowSoftness = Presets.Define(
        CVar.Float(
            "r_shadow_softness",
            1f,
            1f,
            4f,
            CVarOptions.Archive,
            "Shadow filter radius in texels. Above 1.5 a wider, slower kernel is used."),
        vanilla: "1",
        enhanced: "1",
        high: "1.5",

        // 3 at ultra. The reference's sun casts a soft penumbra -- a window's shadow grid lands on the
        // floor with edges that widen with distance, not the hard stencil a single-tap shadow gives. A
        // wider filter is what reads as that penumbra; the grid stays defined at this radius rather than
        // smearing away, matching the reference's soft-but-legible shadows against a crisp one.
        ultra: "3");

    /// <summary>How many directions each pixel searches for its horizons, or 0 for no occlusion.</summary>
    /// <remarks>
    /// Follows the same convention as <c>r_fog_quality</c>: the quality dial doubles as the switch,
    /// because a separate on/off would let the two disagree and a preset would then have to set both.
    /// </remarks>
    public static readonly NumericCVar<int> AmbientOcclusion = Presets.Define(
        CVar.Int(
            "r_ao",
            0,
            0,
            8,
            CVarOptions.Archive,
            "Ambient occlusion: directions searched per pixel; 0 turns it off."),

        // Raised, because these are no longer averaged over frames. The horizon search is dithered
        // per pixel AND per frame on the assumption something converges it; with nothing to do that,
        // the only way to a clean result is to take the samples up front.
        //
        // Ultra was eight and is six. The search cost climbs with the square of this (directions AND the
        // per-direction step count both scale with it), so eight is the most expensive stage on a
        // high-resolution frame -- yet with the temporal accumulation and the edge-aware reconstruct
        // behind it, eight, six and four are indistinguishable in a still (measured: 0.09% mean pixel
        // difference between eight and four). Six keeps ultra at high's quality for roughly half the taps.
        vanilla: "0",
        enhanced: "4",
        high: "6",
        ultra: "6");

    /// <summary>How far a surface may occlude another, in metres.</summary>
    public static readonly NumericCVar<float> AmbientOcclusionRadius = Presets.Define(
        CVar.Float(
            "r_ao_radius",
            1.5f,
            0.05f,
            16f,
            CVarOptions.Archive,
            "How far, in metres, one surface can shade another."),
        vanilla: "1.5",
        enhanced: "1.5",
        high: "1.5",
        ultra: "13.61");

    /// <summary>How strongly the occlusion is applied, where 1 is the physical amount.</summary>
    /// <remarks>
    /// Above about one and a half this stops being occlusion and starts removing the picture. The radius
    /// this backend uses is room-scale rather than contact-scale, so the term already overlaps what the
    /// indirect gather computes; multiplying that overlap by three darkened an office corner until the
    /// lockers standing in it dissolved into the noise floor, while the sunlit floor it could not reach
    /// stayed too bright — the two errors then hid each other in any average over the frame. Measured
    /// against the reference across five regions, three scored 8.2 levels of mean error against 5.6 at
    /// the physical amount, and only the physical amount kept the corner readable.
    /// </remarks>
    public static readonly NumericCVar<float> AmbientOcclusionIntensity = Presets.Define(
        CVar.Float(
            "r_ao_intensity",
            1.2f,
            0f,
            4f,
            CVarOptions.Archive,
            "How deeply ambient occlusion darkens; 1 is the physical amount."),
        vanilla: "1.2",
        enhanced: "1.2",
        high: "1.2",
        ultra: "1.2");

    /// <summary>How many samples gather bounced colour, or 0 for no screen-space bounce.</summary>
    /// <remarks>
    /// Off at ultra, where the world-space radiance cache already carries the bounce. Running both stacked
    /// a second, screen-space estimate on top of the cache's -- and its occlusion term read darker than the
    /// cache's own, dragging interiors down a stop while the per-pixel screen march added noise the cache
    /// does not have. High keeps it (2 samples) because high has no radiance cache to carry the bounce.
    /// </remarks>
    public static readonly NumericCVar<int> ScreenSpaceGi = Presets.Define(
        CVar.Int(
            "r_ssgi",
            0,
            0,
            8,
            CVarOptions.Archive,
            "Screen-space bounced light: samples per direction; 0 turns it off."),
        vanilla: "0",
        enhanced: "0",
        high: "2",
        ultra: "0");

    /// <summary>How strongly the bounced colour is added back.</summary>
    public static readonly NumericCVar<float> ScreenSpaceGiIntensity = Presets.Define(
        CVar.Float(
            "r_ssgi_intensity",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "How strongly surfaces bounce their colour onto each other."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "2");

    /// <summary>How far, in metres, the camera is focused, or 0 for no depth of field.</summary>
    /// <remarks>
    /// Off by default and deliberately so: in a first-person shooter the player's eye chooses what to
    /// look at, and a renderer that blurs everything else is fighting them. Its place is the menu
    /// camera and the sniper scope, which decide where the eye is going anyway.
    /// </remarks>
    public static readonly NumericCVar<float> FocalDistance = Presets.Define(
        CVar.Float(
            "r_dof",
            0f,
            0f,
            500f,
            CVarOptions.Archive,
            "Focal distance in metres; 0 turns depth of field off."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "40");

    /// <summary>Over how many metres a surface goes from sharp to fully defocused.</summary>
    public static readonly NumericCVar<float> FocalRange = Presets.Define(
        CVar.Float(
            "r_dof_range",
            12f,
            0.5f,
            200f,
            CVarOptions.Archive,
            "Metres from the focal plane over which a surface defocuses completely."),
        vanilla: "12",
        enhanced: "12",
        high: "12",
        ultra: "18");

    /// <summary>The widest the defocus gets, in pixels.</summary>
    public static readonly NumericCVar<float> FocalBlur = Presets.Define(
        CVar.Float(
            "r_dof_blur",
            8f,
            0f,
            64f,
            CVarOptions.Archive,
            "The widest defocus, in pixels."),
        vanilla: "8",
        enhanced: "8",
        high: "8",
        ultra: "1");

    /// <summary>How much of a frame's movement is smeared.</summary>
    public static readonly NumericCVar<float> MotionBlur = Presets.Define(
        CVar.Float(
            "r_motion_blur",
            0f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much of a frame's movement smears; a real shutter is open for part of a frame."),

        // Ultra only, by choice rather than by cost. The motion target now carries exact per-vertex
        // motion for skinned bodies -- limbs and the held weapon smear along their own travel, which
        // is what once made this unshippable -- so the blur is correct everywhere. It stays off below
        // ultra because a crisp frame reads better in play; the cvar works at every preset.
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0.4");

    /// <summary>How many samples the defocus and motion gather takes.</summary>
    public static readonly NumericCVar<int> CameraTaps = Presets.Define(
        CVar.Int(
            "r_camera_taps",
            6,
            1,
            16,
            CVarOptions.Archive,
            "Samples per pixel for depth of field and motion blur together."),
        vanilla: "6",
        enhanced: "4",
        high: "6",
        ultra: "10");

    /// <summary>How strongly the derived surface-detail maps are applied, or 0 to ignore them.</summary>
    /// <remarks>
    /// <para>
    /// I.G.I. ships a diffuse texture per surface and nothing else — no normal map, no roughness map.
    /// A physically-based shading model has four inputs and the data answers one, so the rest are
    /// derived from the diffuse itself and registered per texture. This is how far to trust that.
    /// </para>
    /// <para>
    /// <b>Invented data, and a deliberate divergence.</b> It is off under vanilla and enhanced, because
    /// those presets exist to reproduce and to gently improve what shipped rather than to add detail
    /// nobody painted. It is what makes brick read as brick when the sun crosses it, which is a larger
    /// visible gap between this renderer and a modern one than any amount of indirect lighting.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> MaterialDetail = Presets.Define(
        CVar.Float(
            "r_material_detail",
            0f,
            0f,
            2f,
            CVarOptions.Archive,
            "How strongly derived normal and roughness maps are applied; 0 ignores them."),
        vanilla: "0",
        enhanced: "0",

        // Deliberately mild. The maps are DERIVED from tiny diffuse textures (8x8 to 128x128), and at full
        // strength their Scharr normals turn every surface into grainy, sparkling relief that catches the
        // sun, the local lights and the screen-space reflections -- the "everything is reflective and
        // blurry" fault. Measured on mission 1's interiors: relief still reads at a quarter, and the noise
        // only sets in past a third. Full strength was calibrated on one brick wall in isolation and does
        // not survive a whole room. The generator itself (MaterialMapGenerator) wants gentler normals; until
        // then this is where the strength is held.
        high: "0.25",

        // Off at ultra. The reference the ultra look is matched against renders the level's own diffuse
        // textures flat, with no derived normal or roughness, so the derived relief -- however mild -- reads
        // as a departure from it: a faint sparkle on surfaces the reference leaves matte. With the relief
        // gone the two agree, and the low-frequency lighting the radiance cache carries is what the eye reads
        // instead. High keeps a quarter for the presets not held to that reference.
        ultra: "0");

    /// <summary>How glossy an up-facing surface (a floor, a desk top) is made, so it takes a reflection.</summary>
    /// <remarks>
    /// The game ships one roughness for every surface, so a floor is as matte as a wall and reflects nothing,
    /// where the reference render's floor is semi-glossy and mirrors the windows. This multiplies an up-facing
    /// surface's roughness down towards a sheen while leaving the walls matte -- the split the reference has --
    /// keyed off how far the surface faces the world up. One is off, every surface as its material said. Only
    /// the dynamic model reflects, so it does nothing under the others.
    /// </remarks>
    public static readonly NumericCVar<float> FloorGloss = Presets.Define(
        CVar.Float(
            "r_floor_gloss",
            1f,
            0.05f,
            1f,
            CVarOptions.Archive,
            "Roughness multiplier for up-facing surfaces, so floors reflect while walls stay matte. 1 is off."),
        vanilla: "1",
        enhanced: "1",
        high: "1",

        // Off. The screen-space reflection a glossy floor takes cannot describe a thin object -- a chair's
        // legs, a desk edge -- and smeared them across the floor as hard dark streaks, worse than the matte
        // floor it replaced. A floor sheen wants a reflection that sees off-screen geometry cleanly, which the
        // screen trace is not; left off until the probe field can carry the specular instead.
        ultra: "1");

    /// <summary>Where the derived surface-detail maps are read from.</summary>
    /// <remarks>
    /// A directory of PNGs named after the textures they belong to, as
    /// <c>ResExtract maps</c> writes. Empty means there is no pack and nothing is derived, which is
    /// what an installation that has never run the tool gets — and it renders exactly as it did before
    /// the maps existed rather than failing.
    /// </remarks>
    public static readonly StringCVar MaterialPack = CVar.Text(
        "r_material_pack",
        string.Empty,
        CVarOptions.Archive | CVarOptions.RequiresRestart,
        "Directory of derived surface-detail maps; empty for none.");

    /// <summary>Whether every model's surface is captured into the card atlas at load.</summary>
    /// <remarks>
    /// The structure that lets light bounce off geometry the camera cannot see: a ray hitting a model
    /// reads its captured surface rather than needing the model drawn. Off at every preset until the
    /// stages that read it exist — an atlas nothing samples is load time and video memory spent on
    /// nothing.
    /// </remarks>
    public static readonly BoolCVar SurfaceCache = Presets.Define(
        CVar.Bool(
            "r_cards",
            false,
            CVarOptions.Archive | CVarOptions.RequiresRestart,
            "Capture every model's surface into the card atlas at load."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // Ultra's real-time global illumination is built on the surface cache: the screen probe gather
        // traces it for the indirect fill, so ultra is the one preset that pays to capture it. The other
        // three light from the bakes and never read a card.
        ultra: "1");

    /// <summary>How many texels of card a world unit of surface is given.</summary>
    /// <remarks>
    /// The dial that decides both the atlas size and how much detail a bounce carries. Higher resolves
    /// a doorway rather than averaging over a wall, and costs the square of itself.
    /// </remarks>
    public static readonly NumericCVar<float> SurfaceCacheDensity = Presets.Define(
        CVar.Float(
            "r_cards_density",
            8f,
            1f,
            64f,
            CVarOptions.Archive | CVarOptions.RequiresRestart,
            "Texels of card per world unit of surface."),
        vanilla: "8",
        enhanced: "8",
        high: "8",
        ultra: "23.05");

    /// <summary>Where to write the captured atlas as images, or empty to write none.</summary>
    /// <remarks>
    /// A capture can only be judged by looking at it. Wrong winding shows as models captured
    /// inside-out, a wrong card rotation as mirrored pages, a missing scissor as pages bleeding into
    /// each other — and every one of those produces an atlas full of entirely plausible surfaces.
    /// </remarks>
    public static readonly StringCVar SurfaceCacheDump = CVar.Text(
        "r_cards_dump",
        string.Empty,
        CVarOptions.Developer,
        "Directory to write the captured card atlas to as PNGs; empty writes none.");

    /// <summary>What the lighting pass writes into its atlas: light, or a chosen diagnostic.</summary>
    /// <remarks>
    /// A shadow trace that fails leaves nothing to look at — a black atlas is equally consistent with a
    /// wrong world position, an unbound sampler, a uniform that never arrived and a field that is
    /// genuinely closed over. Each probe writes one of those intermediates where the light would have
    /// gone, so the dump can tell them apart: 1 writes what the trace sees at its start point, 2 the
    /// reconstructed world position within the outermost clipmap, 3 the world normal, 4 how the trace
    /// ended, 5 whether the card index sends a texel back to itself.
    /// <para>
    /// <b>Keep the maximum in step with the shader.</b> This clamps, silently and by design, so a probe
    /// added to the shader but not added here runs as whichever probe the clamp lands on -- which
    /// presents as the new probe reporting nothing whatsoever, and is indistinguishable from the thing
    /// it was measuring being broken.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> SurfaceCacheProbe = CVar.Int(
        "r_cards_probe",
        0,
        0,
        5,
        CVarOptions.Developer,
        "Diagnostic the card lighting writes instead of light: 0 none, 1 trace start, 2 position, "
        + "3 normal, 4 trace outcome, 5 card index self-test.");

    /// <summary>Whether the level's signed distance fields are baked and resampled into clipmaps.</summary>
    /// <remarks>
    /// <para>
    /// The structure everything in the realtime GI track traces through. A ray marched against
    /// triangles tests every triangle it might cross; a ray marched against a distance field asks how
    /// far the nearest surface is and steps exactly that far, which cannot overshoot.
    /// </para>
    /// <para>
    /// Needs compute, so it is off wherever the context cannot honour it whatever this says. Off at
    /// every preset until the stages that consume it exist — a field nothing traces is a load-time cost
    /// with nothing to show for it.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar DistanceFields = Presets.Define(
        CVar.Bool(
            "r_sdf",
            false,
            CVarOptions.Archive | CVarOptions.RequiresRestart,
            "Bake the level's signed distance fields and keep clipmaps of them around the camera."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // The screen probe gather traces the distance fields for the rays that leave the screen, so ultra
        // bakes them for the same reason it captures the cards. The cheaper presets need neither.
        ultra: "1");

    /// <summary>How many voxels the longest axis of each model's distance field gets.</summary>
    /// <remarks>
    /// The cost of a bake is roughly this cubed per distinct model, and the memory exactly so. Thirty-two
    /// gives this game's crates and huts voxels of a few centimetres, which is fine enough for a trace
    /// to land on the right side of a wall.
    /// </remarks>
    public static readonly NumericCVar<int> DistanceFieldResolution = Presets.Define(
        CVar.Int(
            "r_sdf_resolution",
            32,
            8,
            128,
            CVarOptions.Archive | CVarOptions.RequiresRestart,
            "Voxels along the longest axis of each model's distance field."),
        vanilla: "32",
        enhanced: "32",
        high: "32",
        ultra: "46");

    /// <summary>Whether the built clipmaps are checked against the exact field once, after a load.</summary>
    /// <remarks>
    /// Reads the clipmap back and compares every voxel against <c>SceneDistanceField</c>, which answers
    /// the same question exactly. Costs a synchronising readback and a few hundred thousand CPU
    /// evaluations, so it is a diagnostic rather than something to leave on — but a wrong distance field
    /// is invisible in a rendered frame, and this is the only thing that can see it.
    /// </remarks>
    public static readonly BoolCVar DistanceFieldValidate = CVar.Bool(
        "r_sdf_validate",
        false,
        CVarOptions.Developer,
        "After a load, compare the built clipmap against the exact field and report the difference.");

    /// <summary>Scales the whole dynamically-computed irradiance.</summary>
    /// <remarks>
    /// Not a taste control, a calibration. I.G.I.'s diffuse textures are not albedo: they were painted
    /// to be multiplied by a lightmap averaging well below one, so they already carry lighting.
    /// Lighting them at unit irradiance counts that painted-in brightness twice, and it lands hardest
    /// on the brightest textures -- which is why the sand and the snow blew out while the concrete
    /// looked right. The default is measured against the artists' own bakes rather than chosen.
    /// </remarks>
    public static readonly NumericCVar<float> LightScale = Presets.Define(
        CVar.Float(
            "r_light_scale",

            // Seven eighths lands mission 1's authored sun — about eight lux along its warm chromaticity —
            // on the reference scene's seven, so the two renders share an illuminance and every remaining
            // difference is the transport's fault, not the key light's. Ultra carried a hand-set 0.96 for
            // a while, about a tenth of a stop over; measured against the reference across five regions
            // it was worth about half a level of error, and the derivation is the better authority.
            0.875f,
            0.1f,
            2f,
            CVarOptions.Archive,
            "Scales dynamic lighting to match what the level's textures were painted for."),
        vanilla: "0.875",
        enhanced: "0.875",
        high: "0.875",
        ultra: "0.875");

    /// <summary>Scales the flat ambient stand-in the dynamic model fills unlit surfaces with.</summary>
    /// <remarks>
    /// TEMPORARY. Until the probe volume of stage R7 gives ambient a sense of place, the dynamic model
    /// lights every surface with one flat ambient colour from the level's Dirlight -- so a sealed,
    /// windowless room reads as bright as open ground. This scales that stand-in so the effect can be
    /// dialled down to see enclosed spaces go dark, or to nothing (0) to prove it is what lights them.
    /// Not archived and not part of any preset: it exists to diagnose, not to ship. Only the dynamic
    /// model reads it -- the baked model lights from the lightmaps and has no ambient term of its own.
    /// </remarks>
    public static readonly NumericCVar<float> AmbientScale = CVar.Float(
        "r_ambient_scale",
        0f,
        0f,
        2f,
        CVarOptions.None,
        "Scales the dynamic model's flat ambient fill; 0 removes it and lets the GI carry the indirect. Only the dynamic model reads it.");

    /// <summary>Scales the real-time sun so a sunlit surface reads as sunlit rather than as flat daylight.</summary>
    /// <remarks>
    /// The artists' lightmaps carry the sun at its full radiosity intensity (18 in every mission), so the
    /// baked model has bright, high-contrast sunlight for free. The dynamic model adds the sun live from
    /// its colour alone -- magnitude about one -- which is barely stronger than the ambient sky fill, so a
    /// sunlit wall reads the same as a shaded one and the whole scene looks overcast rather than sunny.
    /// This lifts the live sun back to where the contrast reads as direct sunlight. Only the dynamic model
    /// touches it: the baked model lights from the lightmaps and never adds a live sun at all.
    /// </remarks>
    public static readonly NumericCVar<float> SunScale = Presets.Define(
        CVar.Float(
            "r_sun_scale",
            1f,
            0.25f,
            16f,
            CVarOptions.Archive,
            "Scales the real-time sun so sunlit surfaces read as sunlit. Only the dynamic lighting model reads it."),
        vanilla: "1",
        enhanced: "1",
        high: "1",

        // 7 at ultra. The old 2 left the sun barely brighter than the indirect fill, so its shafts washed
        // into an evenly-lit floor -- nothing like the reference, whose bright window pattern reads clearly
        // against a darker floor. A real daylight sun is far more intense than the room's bounce; scaled up
        // to that, the direct light dominates the surfaces it lands on and its shafts read as shafts, while
        // the sky fill (dialled DOWN to match, see r_sky_light) no longer floods the shadow between them.
        // Auto-exposure adapts to the brighter key, so the frame does not simply blow -- measured on the
        // office (shafts crisp, walls held) and the outdoor spawn (bright, not clipped).
        ultra: "7");

    /// <summary>How strongly edges in the finished frame are smoothed, or 0 for no anti-aliasing.</summary>
    /// <remarks>
    /// Spatial, and therefore free of ghosting: it reads the displayed picture and nothing else. With
    /// the temporal path repaired it is a choice rather than the only option — the ghost-free
    /// alternative for anyone who dislikes accumulation — and the presets that run TAA leave it off,
    /// because smoothing an already-converged frame only softens it a second time.
    /// </remarks>
    public static readonly NumericCVar<float> SpatialAa = Presets.Define(
        CVar.Float(
            "r_aa",
            0f,
            0f,
            1f,
            CVarOptions.Archive,
            "Edge anti-aliasing strength; 0 turns it off. Spatial, so it never ghosts."),
        vanilla: "0",
        enhanced: "0.75",
        high: "0",
        ultra: "0");

    /// <summary>The contrast below which a pixel is left untouched.</summary>
    public static readonly NumericCVar<float> SpatialAaThreshold = Presets.Define(
        CVar.Float(
            "r_aa_threshold",
            0.05f,
            0.005f,
            0.5f,
            CVarOptions.Archive,
            "Luminance contrast below which a pixel is not treated as an edge."),
        vanilla: "0.05",
        enhanced: "0.05",
        high: "0.05",
        ultra: "0.33");

    /// <summary>How much of the blurred bright pass is mixed back in, or 0 for no bloom.</summary>
    public static readonly NumericCVar<float> Bloom = Presets.Define(
        CVar.Float(
            "r_bloom",
            0f,
            0f,
            2f,
            CVarOptions.Archive,
            "How much light spills out of the bright parts of the frame; 0 turns it off."),
        vanilla: "0",
        enhanced: "0.25",
        high: "0.35",
        ultra: "0.45");

    /// <summary>The luminance above which a pixel starts to bloom.</summary>
    public static readonly NumericCVar<float> BloomThreshold = Presets.Define(
        CVar.Float(
            "r_bloom_threshold",
            1f,
            0.1f,
            8f,
            CVarOptions.Archive,
            "The brightness a pixel has to pass before it spills."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "2.47");

    /// <summary>How far colour is pushed away from mid-grey.</summary>
    public static readonly NumericCVar<float> Contrast = Presets.Define(
        CVar.Float(
            "r_contrast",
            1f,
            0.25f,
            2f,
            CVarOptions.Archive,
            "Contrast, where 1 leaves the picture alone."),
        vanilla: "1",
        enhanced: "1.04",
        high: "1.06",
        ultra: "1.08");

    /// <summary>How far colour is pushed away from its own luminance.</summary>
    public static readonly NumericCVar<float> Saturation = Presets.Define(
        CVar.Float(
            "r_saturation",
            1f,
            0f,
            2f,
            CVarOptions.Archive,
            "Colour saturation, where 1 leaves the picture alone and 0 is greyscale."),
        vanilla: "1",
        enhanced: "1.05",
        high: "1.08",
        ultra: "1.1");

    /// <summary>How much the corners of the frame are darkened.</summary>
    public static readonly NumericCVar<float> Vignette = Presets.Define(
        CVar.Float(
            "r_vignette",
            0f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much the corners darken. Every lens does this; its absence reads as rendered."),
        vanilla: "0",
        enhanced: "0.25",
        high: "0.3",
        ultra: "0.35");

    /// <summary>How much film grain is added.</summary>
    public static readonly NumericCVar<float> Grain = Presets.Define(
        CVar.Float(
            "r_grain",
            0f,
            0f,
            0.2f,
            CVarOptions.Archive,
            "Film grain. A little also hides the banding an 8-bit display gives a smooth gradient."),

        // Off. It was added on the assumption that the temporal accumulation would keep it to a
        // suggestion; with no accumulation it is one more source of noise on top of the stochastic
        // passes, and the thing it was hiding -- 8-bit banding -- is the lesser complaint.
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0");

    /// <summary>How far the red and blue channels separate towards the corners.</summary>
    public static readonly NumericCVar<float> ChromaticAberration = Presets.Define(
        CVar.Float(
            "r_chromatic",
            0f,
            0f,
            8f,
            CVarOptions.Archive,
            "Lens colour fringing at the corners, in pixels. Zero in the middle however high it is."),
        vanilla: "0",
        enhanced: "0",
        high: "0.8",

        // Off at ultra. The reference the ultra look is matched against has no lens fringing, and against a
        // window's high-contrast edges the aberration read as coloured fringes that made the frame look less
        // clean, not more filmic. High keeps a little for the presets not held to that reference.
        ultra: "0");

    /// <summary>How much of the level's authored colour table is applied.</summary>
    /// <remarks>
    /// <para>
    /// Per mission and per preset. A mission's grade is loaded from <c>Assets/luts/&lt;name&gt;.cube</c>
    /// when one exists, and this is how much of it to use — the same relationship the fog presets have
    /// with <c>r_fog_density</c>. Zero at vanilla, because vanilla is the game as it shipped and it
    /// shipped ungraded.
    /// </para>
    /// <para>
    /// Below one on purpose at the middle presets. A grade authored against a clean daylight frame is
    /// usually too strong once the same frame is also carrying volumetric fog and bloom, and the levels
    /// this game ships are already strongly coloured by their own lightmaps.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> ColourLookup = Presets.Define(
        CVar.Float(
            "r_lut",
            0f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much of the mission's authored colour grade to apply; 0 turns it off."),
        vanilla: "0",
        enhanced: "0.5",
        high: "0.75",
        ultra: "1");

    /// <summary>The colour table to use instead of the mission's own, or empty for the mission's own.</summary>
    /// <remarks>
    /// An override for authoring, not a setting: naming a table here loads
    /// <c>Assets/luts/&lt;name&gt;.cube</c> whatever the mission is, which is how a grade is compared
    /// against another one without restarting into a different level. <c>identity</c> is always
    /// available and is the table that changes nothing — the one that shows whether the sampler's
    /// coordinates are right, since with it any visible change at all is a bug.
    /// </remarks>
    public static readonly StringCVar ColourLookupTable = CVar.Text(
        "r_lut_table",
        string.Empty,
        CVarOptions.Developer,
        "Load Assets/luts/<name>.cube instead of the mission's own grade. Empty uses the mission's.");

    /// <summary>How much local contrast is restored after the temporal accumulation softens the frame.</summary>
    public static readonly NumericCVar<float> Sharpen = Presets.Define(
        CVar.Float(
            "r_sharpen",
            0f,
            0f,
            1f,
            CVarOptions.Archive,
            "Restores local contrast lost to temporal accumulation and upsampling."),
        vanilla: "0",
        enhanced: "0.1",
        high: "0.15",
        ultra: "0.2");

    /// <summary>How many steps a reflected ray takes, or 0 for the fallback alone.</summary>
    public static readonly NumericCVar<int> Reflections = Presets.Define(
        CVar.Int(
            "r_ssr",
            0,
            0,
            256,
            CVarOptions.Archive,
            "Screen-space reflections: steps per reflected ray; 0 leaves only the probe fallback."),
        vanilla: "0",
        enhanced: "0",
        high: "32",
        ultra: "64");

    /// <summary>How strongly reflections are mixed in.</summary>
    public static readonly NumericCVar<float> ReflectionIntensity = Presets.Define(
        CVar.Float(
            "r_ssr_intensity",
            1f,
            0f,
            2f,
            CVarOptions.Archive,
            "How strongly surfaces reflect; 1 is the physical amount."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1.7");

    /// <summary>The roughness past which a surface stops marching a screen ray and keeps the smooth fallback.</summary>
    /// <remarks>
    /// Held low deliberately. A screen-marched reflection is a SINGLE ray, so on anything but a near-mirror
    /// it reads the one surface that ray happens to hit -- for a parquet floor or a painted wall that is a
    /// dark, shadowed patch of the room, which the march then substitutes for the smooth probe fallback and
    /// the surface CRUSHES a stop or two darker than it should, besides shimmering as the ray wanders under
    /// motion. That single-ray march is what made interiors read dark and noisy. At 0.22 only genuinely
    /// glossy surfaces -- glass, a monitor, polished metal -- still march, where one ray IS the reflection;
    /// everything rougher keeps the probe field's smooth, bright, stable fallback. The reference denoises a
    /// many-ray reflection instead; this is the single-ray equivalent of not marching where it cannot help.
    /// </remarks>
    public static readonly NumericCVar<float> ReflectionMaxRoughness = Presets.Define(
        CVar.Float(
            "r_ssr_max_roughness",
            0.22f,
            0f,
            1f,
            CVarOptions.Archive,
            "Roughest surface that still marches a screen ray; rougher ones keep the smooth probe fallback."),
        vanilla: "0.22",
        enhanced: "0.22",
        high: "0.22",
        ultra: "0.22");

    /// <summary>Whether surfaces take their indirect light from the baked probe field.</summary>
    /// <remarks>
    /// Costs a trace of the level at load rather than anything per frame, which is why it is a
    /// restart-scope setting in practice even though nothing enforces that: turning it on mid-level
    /// finds no field baked and changes nothing until the next load.
    /// </remarks>
    public static readonly BoolCVar GlobalIllumination = Presets.Define(
        CVar.Bool(
            "r_gi",
            false,
            CVarOptions.Archive,
            "Light surfaces from the level's baked probe field instead of a flat ambient term."),
        vanilla: "0",
        enhanced: "0",
        high: "1",

        // On at ultra, and read there. The real-time screen gather no longer replaces this field -- it is
        // FLOORED against it (see the deferred fill's max of the two), so this is the coarse, visibility-aware
        // far field a surface the screen probes cannot resolve falls back to instead of black: a merlon or a
        // wire thinner than the probe spacing, or a wall the screen never saw. High and below, with no
        // surface cache to gather, light from this bake directly.
        ultra: "1");

    /// <summary>How the level's static object lightmaps are sourced: shipped, augmented, or our bake alone.</summary>
    /// <remarks>
    /// <para>
    /// <b>0</b> uses the shipped <c>.olm</c> object lightmaps, exactly as retail. <b>1</b> re-bakes them with
    /// the path tracer at load -- per-texel, coloured, multi-bounce, from the level's own sun and sky -- and
    /// swaps a baked page in wherever there is one, falling back to the shipped page where the bake produced
    /// nothing. <b>2</b> discards the shipped lightmaps entirely: a surface the bake did not reach reads
    /// black rather than borrowing the artists' light, so what the frame shows is ONLY our own lighting --
    /// the honest state of the system, gaps and all.
    /// </para>
    /// <para>
    /// A restart-scope setting: the bake runs once at load (on a worker, cached to disk) and only ever
    /// reaches the baked and hybrid models, which sample the lightmap; vanilla and the dynamic path are
    /// untouched. Interiors a sealed room's sun cannot reach stay dark until the lamp fixtures are baked.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> BakedLightmaps = Presets.Define(
        CVar.Int(
            "r_lightmap",
            0,
            0,
            2,
            CVarOptions.Archive,
            "Object lightmaps: 0 shipped, 1 path-traced bake over the shipped ones, 2 path-traced bake only."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // Ultra runs OUR lighting only -- it discards the shipped .olm entirely and shows the path-traced
        // bake alone, so what the frame lights with is the level's own sun and sky computed rather than the
        // artists' hand-painted maps. A room the bake cannot reach reads dark rather than borrowing their
        // light; that is the honest state until the lamp fixtures are baked as emitters.
        ultra: "2");

    /// <summary>How far apart the probes sit, in metres.</summary>
    public static readonly NumericCVar<float> GlobalIlluminationSpacing = Presets.Define(
        CVar.Float(
            "r_gi_probe_spacing",
            6f,
            1f,
            32f,
            CVarOptions.Archive,
            "Metres between irradiance probes. Finer resolves smaller rooms and takes longer to bake."),
        vanilla: "6",
        enhanced: "6",
        high: "6",
        ultra: "3");

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
        enhanced: "0.3",
        high: "0.3",
        ultra: "0");

    /// <summary>Whether the inferred lamp fixtures light the surface cache, so interiors lit by their own lamps.</summary>
    /// <remarks>
    /// The level declares no point lights; a survey reads them from emissive geometry -- the ceiling tubes
    /// and the like -- and feeds them into the surface cache so a lamplit room bounces. Off by default: the
    /// survey is not yet reliable (a bright sign or a screen can read as a lamp), and while it is wrong a
    /// sealed windowless room lit by an inferred lamp cannot be told from one that ought to be dark. With it
    /// off such a room reads dark, which is the honest state until the survey is dialled in. On lights
    /// interiors from their fixtures.
    /// </remarks>
    public static readonly NumericCVar<int> GlobalIlluminationLamps = Presets.Define(
        CVar.Int(
            "r_gi_lamps",
            1,
            0,
            1,
            CVarOptions.Archive,
            "Light the surface cache from the level's inferred lamp fixtures, so lamplit rooms bounce."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How strongly the probe field's light is applied.</summary>
    public static readonly NumericCVar<float> GlobalIlluminationIntensity = Presets.Define(
        CVar.Float(
            "r_gi_intensity",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "How strongly the probe field lights surfaces; 1 is the amount the bake computed."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "4");

    /// <summary>The diffuse colour boost the surface cache reflects light through — the reference's DiffuseColorBoost.</summary>
    /// <remarks>
    /// The single lever for how bright the bounced light is. Each time light reflects off a card, the
    /// surface's albedo is raised by <c>pow(luminance, boost)</c> before it passes light along, so a
    /// <b>lower</b> value lifts a dark, low-albedo surface's reflectance towards one and lets a sunless
    /// interior carry far more indirect -- which the raw retail albedos (~0.4) otherwise swallow after a
    /// bounce or two. One is off (physical albedo). It never reaches one (the surface would return
    /// everything it received and the feedback would diverge); the shader clamps it below. This is what to
    /// reach for when interiors read dark, ahead of exposure or more bounces.
    /// </remarks>
    public static readonly NumericCVar<float> GlobalIlluminationBoost = Presets.Define(
        CVar.Float(
            "r_gi_boost",
            0.8f,
            0.1f,
            1f,
            CVarOptions.Archive,
            "Diffuse colour boost for bounced light: LOWER lifts dark surfaces so interiors carry more indirect. 1 is off."),
        vanilla: "0.8",
        enhanced: "0.8",
        high: "0.8",
        ultra: "0.6");

    /// <summary>Whether each stage of the frame is timed on the GPU and shown in an overlay.</summary>
    /// <remarks>
    /// <para>
    /// The instrument the remaster track's frame budget is checkable with, and it did not exist for the
    /// five stages that were written against it: shadows ≤1.0 ms, occlusion ≤1.0, lighting ≤2.0,
    /// reflections ≤1.5, fog ≤1.5, post ≤1.5. Every performance claim made before this was an estimate.
    /// </para>
    /// <para>
    /// Developer rather than archived, and off by default, because it is not free: a timer query is a
    /// synchronisation point in the command stream, so measuring the frame does change it a little. It
    /// is close enough to judge a budget by and not close enough to leave on.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar PerformanceLog = CVar.Bool(
        "r_perflog",
        false,
        CVarOptions.Developer,
        "Time each stage of the frame on the GPU and show the result. Costs a little of what it measures.");

    /// <summary>Whether the load-time GPU compute diagnostic is run.</summary>
    /// <remarks>
    /// The diagnostic compiles temporary shaders and reads their results back to the CPU, which makes it
    /// useful when bringing up a driver but needlessly delays every ordinary launch. It therefore stays
    /// available to developers without being part of the player's startup path.
    /// </remarks>
    public static readonly BoolCVar ComputeSelfTest = CVar.Bool(
        "r_compute_selftest",
        false,
        CVarOptions.Developer,
        "Run the load-time GPU compute diagnostic. Off avoids a blocking driver synchronization at startup.");

    /// <summary>Which buffer to show instead of the scene.</summary>
    public static readonly EnumCVar<DebugView> Debug = CVar.Choice(
        "r_debug_view",
        DebugView.None,
        CVarOptions.Developer,
        "Show a render buffer instead of the scene: none, albedo, normal, depth, material, motion, bakedlight.");

    /// <summary>How much of the resolved pixel is this frame, or 1 for no accumulation.</summary>
    /// <remarks>
    /// The lower it is the more samples a pixel holds and the cleaner it converges, but the longer it
    /// takes to notice that the surface under it has changed. It is also what pays for the noise in
    /// every stochastic pass after this one, so it is a preset dial rather than a fixed constant.
    /// </remarks>
    public static readonly NumericCVar<float> TemporalFeedback = Presets.Define(
        CVar.Float(
            "r_taa",
            1f,
            0.02f,
            1f,
            CVarOptions.Archive,
            "Temporal anti-aliasing: share of the pixel taken from this frame. 1 turns it off."),

        // Back on at high and ultra. It was off at every preset because soldiers are SKINNED and
        // their motion was reported as a rigid transform -- "where the body was, with the arm where
        // it is now" -- a confidently wrong vector no feedback value could paper over. The G-buffer
        // now takes the previous clip position from the previous frame's skinned VERTICES
        // (CreateAnimatedMesh's second stream), so a walking guard's limbs reproject exactly and the
        // smear that forced this off no longer exists.
        //
        // The accumulation is also what pays for the noise in every stochastic pass after it -- the
        // fog march, the horizon search, the reflection ray are all under-sampled on the assumption
        // that this averages them. Ultra holds a longer history than high because it runs more of
        // those passes at higher counts and benefits more from the convergence.
        vanilla: "1",
        enhanced: "1",
        high: "0.15",
        ultra: "0.1");

    /// <summary>How much of the history's resampling blur is subtracted back out.</summary>
    public static readonly NumericCVar<float> TemporalSharpening = Presets.Define(
        CVar.Float(
            "r_taa_sharpen",
            0.25f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much of the temporal history's resampling blur is sharpened back out."),
        vanilla: "0.25",
        enhanced: "0.25",
        high: "0.25",
        ultra: "0.7");

    /// <summary>Whether cut-outs use a temporally-resolved hashed alpha test rather than a hard one.</summary>
    /// <remarks>
    /// A hard cut-out keeps or drops whole pixels, so a chain-link fence or a wire thinner than a pixel
    /// aliases into a shimmer. The hashed test recovers the edge's coverage from the alpha gradient and
    /// dithers it, which the temporal resolve then averages into a smooth edge. It therefore only helps
    /// where the temporal filter runs, so it follows the same presets: off where <see cref="TemporalFeedback"/>
    /// is one, on above it.
    /// </remarks>
    public static readonly BoolCVar HashedAlpha = Presets.Define(
        CVar.Bool(
            "r_alpha_hash",
            false,
            CVarOptions.Archive,
            "Dither cut-out edges and let the temporal resolve average them, instead of a hard alpha test."),
        vanilla: "0",
        enhanced: "0",
        high: "1",
        ultra: "1");

    /// <summary>How many samples each fog ray takes, or 0 for no volumetric fog.</summary>
    /// <remarks>The single biggest cost in the pass, and the quality dial.</remarks>
    public static readonly NumericCVar<int> FogQuality = Presets.Define(
        CVar.Int(
            "r_fog_quality",
            32,
            0,
            128,
            CVarOptions.Archive,
            "Samples per volumetric fog ray; 0 turns the fog off."),

        // Likewise: the march's interleaved-gradient dither used to be averaged away, and now is not.
        vanilla: "0",
        enhanced: "32",
        high: "64",
        ultra: "96");

    /// <summary>Extinction per metre at the fog's base height, or 0 to take the level's own.</summary>
    public static readonly NumericCVar<float> FogDensity = Presets.Define(
        CVar.Float(
            "r_fog_density",
            0f,
            0f,
            0.2f,
            CVarOptions.Archive,
            "Fog extinction per metre at its base height; 0 derives it from the level's own FlatSky data."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0.11");

    /// <summary>The height in metres over which fog density falls to a third.</summary>
    /// <remarks>What makes fog pool in valleys rather than hang as a flat sheet.</remarks>
    public static readonly NumericCVar<float> FogHeightFalloff = Presets.Define(
        CVar.Float(
            "r_fog_height",
            24f,
            1f,
            400f,
            CVarOptions.Archive,
            "Height in metres over which fog thins out. Lower pools it harder into valleys."),
        vanilla: "24",
        enhanced: "24",
        high: "24",
        ultra: "240.4");

    /// <summary>How strongly the fog scatters forward.</summary>
    public static readonly NumericCVar<float> FogAnisotropy = Presets.Define(
        CVar.Float(
            "r_fog_anisotropy",
            0.7f,
            -0.95f,
            0.95f,
            CVarOptions.Archive,
            "Forward scattering, -0.95 to 0.95. Positive makes fog glow towards the sun."),
        vanilla: "0.7",
        enhanced: "0.7",
        high: "0.7",
        ultra: "-0.38");

    /// <summary>How far, in metres, the fog is marched before the cheaper haze takes over.</summary>
    public static readonly NumericCVar<float> FogDistance = Presets.Define(
        CVar.Float(
            "r_fog_distance",
            500f,
            50f,
            2000f,
            CVarOptions.Archive,
            "How far volumetric fog is marched, in metres."),
        vanilla: "500",
        enhanced: "350",
        high: "500",
        ultra: "800");

    /// <summary>The anisotropic filtering ratio, 1 for none.</summary>
    /// <remarks>
    /// The single cheapest visible win in this renderer. A ground plane at a grazing angle is the
    /// worst case trilinear filtering has: a pixel's footprint is a long thin sliver, the mip level
    /// has to be chosen for its widest axis, and all the detail along the other axis is discarded.
    /// That is why terrain smears towards the horizon without it.
    /// </remarks>
    public static readonly NumericCVar<int> Anisotropy = Presets.Define(
        CVar.Int(
            "r_anisotropy",
            8,
            1,
            16,
            CVarOptions.Archive,
            "Anisotropic filtering ratio; 1 is off. Mostly decides how sharp the ground stays into the distance."),
        vanilla: "1",
        enhanced: "4",
        high: "8",
        ultra: "16");

    /// <summary>Whether a mip chain is built for textures that ship without one.</summary>
    /// <remarks>
    /// The terrain's ground tiles ship without: the atlas layer descriptors carry no mip count, so
    /// each is one 128x128 level, and minifying that unfiltered crawls and sparkles.
    /// </remarks>
    public static readonly BoolCVar GenerateMipmaps = Presets.Define(
        CVar.Bool(
            "r_generate_mipmaps",
            true,
            CVarOptions.Archive | CVarOptions.RequiresRestart,
            "Build mipmaps for textures that ship without them, such as the terrain's ground tiles."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether gameplay uses the remaster HUD instead of IGI1's retail StatusScreen.</summary>
    /// <remarks>
    /// Both presentations stay loaded so this can switch live from the console or settings screen.
    /// Vanilla selects the recovered retail path; the other presets retain the remaster HUD by default.
    /// </remarks>
    public static readonly BoolCVar RemasteredHud = Presets.Define(
        CVar.Bool(
            "r_remastered_hud",
            true,
            CVarOptions.Archive,
            "Draw the remaster HUD. Off uses IGI1's original health, weapon, action and timer display."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the C-key map computer is the remaster's interface rather than the retail one.</summary>
    /// <remarks>
    /// <para>
    /// Two entirely separate presentations of the same data. Off is the original's: a low-resolution
    /// <c>mapcomputer.mef</c> panel held up in front of the camera, its aperture filled by a 4:3 screen
    /// set in the game's own bitmap faces. On is a full-screen interface set in the shell's Saira,
    /// reached through a camera move and a phosphor display transform, with no device model at all.
    /// </para>
    /// <para>
    /// Off under vanilla for the reason everything else is: <c>-vanilla</c> and <c>-vanillarender</c>
    /// exist to put the original's picture on screen, and a redesigned computer is not that. The retail
    /// path is kept whole rather than emulated by the new one — it is the regression gate, and a gate
    /// that shares an implementation with the thing it is gating is not a gate.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar TacticalComputer = Presets.Define(
        CVar.Bool(
            "r_computer_tactical",
            true,
            CVarOptions.Archive,
            "Draw the map computer as the full-screen tactical interface. Off is the retail handheld panel."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the remastered binocular applies its glass-lens display transform.</summary>
    /// <remarks>
    /// The optics tint, highlight bloom and bezel vignette are a resolve-time display transform over the
    /// finished picture, raised as the device comes up. Off under vanilla for the reason the tactical
    /// computer is: the retail binocular is the regression gate and must stay untouched.
    /// </remarks>
    public static readonly BoolCVar BinocularLens = Presets.Define(
        CVar.Bool(
            "r_binocular_lens",
            true,
            CVarOptions.Archive,
            "Apply the remastered binocular's glass-lens transform (tint, bloom, bezel vignette)."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether binocular-detected enemies are given a through-wall silhouette glow.</summary>
    /// <remarks>
    /// A team-coloured selection glow drawn over each scanned soldier while the remastered optics are up,
    /// so a marked contact stays legible through cover. Off under vanilla with the rest of the remaster.
    /// </remarks>
    public static readonly BoolCVar BinocularOutline = Presets.Define(
        CVar.Bool(
            "r_binocular_outline",
            true,
            CVarOptions.Archive,
            "Glow binocular-detected enemies through walls while the remastered optics are up."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The remastered binocular detector's range at the default zoom, in metres.</summary>
    /// <remarks>
    /// A modern optics touch: the detector has a finite range rather than tagging every enemy in the
    /// frustum however far off, and that range grows as the operator zooms in -- from this value at the
    /// default field of view up to <see cref="BinocularRangeZoomed"/> at the tightest zoom. The vanilla
    /// optics ignore both and stay retail-uncapped.
    /// </remarks>
    public static readonly NumericCVar<float> BinocularRange = Presets.Define(
        CVar.Float(
            "r_binocular_range",
            150f,
            50f,
            4000f,
            CVarOptions.Archive,
            "Range, in metres, the remastered binocular detector marks a contact at, at the default zoom."),
        vanilla: "150",
        enhanced: "150",
        high: "150",
        ultra: "150");

    /// <summary>The remastered binocular detector's range at the tightest zoom, in metres.</summary>
    /// <remarks>
    /// The range scales from <see cref="BinocularRange"/> at the default field of view to this at the
    /// closest magnification, so leaning into the optics sees further, the way a real scope resolves a
    /// distant figure the naked view cannot.
    /// </remarks>
    public static readonly NumericCVar<float> BinocularRangeZoomed = Presets.Define(
        CVar.Float(
            "r_binocular_range_zoomed",
            250f,
            50f,
            4000f,
            CVarOptions.Archive,
            "Range, in metres, the remastered binocular detector marks a contact at, at the tightest zoom."),
        vanilla: "250",
        enhanced: "250",
        high: "250",
        ultra: "250");

    /// <summary>Whether the mission's status messages are drawn as the remaster's comms cards.</summary>
    /// <remarks>
    /// <para>
    /// Presentation only. Both paths draw the same messages at the same moments, in the same order, with
    /// the same typewriter: when a message arrives is decided by the simulation, and neither renderer can
    /// reach that. Off is the original's placement and bitmap face; on is a card set in the shell's Saira.
    /// </para>
    /// <para>
    /// This preference only applies while <see cref="RemasteredHud"/> is on. The retail HUD always uses
    /// its own bitmap message block and original display timing.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar TacticalMessages = Presets.Define(
        CVar.Bool(
            "r_messages_tactical",
            true,
            CVarOptions.Archive,
            "With the remaster HUD, draw mission messages as tactical cards. Off is the retail message block."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How strongly the screen washes red when the player is hit.</summary>
    /// <remarks>
    /// <para>
    /// A multiplier on the alpha of the full-screen quad <c>HumanPlayer_DrawDamageFlash</c>
    /// (<c>0x4bb770</c>) paints, where 1 is the original's own strength and 0 removes it. The decay is
    /// untouched either way: this changes how red the wash is, not how long it lasts, so the feedback
    /// still tells the player which direction the hit came from and for exactly as long.
    /// </para>
    /// <para>
    /// Below 1 by default outside vanilla. The original's full-strength wash was tuned for a 640x480
    /// display a good distance from the player's face; the same alpha across a modern screen is enough
    /// to hide the thing that is shooting at you, which turns a damage cue into a punishment.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> DamageFlash = Presets.Define(
        CVar.Float(
            "r_damage_flash",
            0.55f,
            0f,
            1f,
            CVarOptions.Archive,
            "How red the screen goes when you are hit. 1 is the original's strength, 0 removes it."),
        vanilla: "1",
        enhanced: "0.55",
        high: "0.55",
        ultra: "0.55");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
