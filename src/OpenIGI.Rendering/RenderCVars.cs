using OpenIGI.Core.Configuration;
using OpenIGI.Rendering.Presets;

namespace OpenIGI.Rendering;

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
    /// <summary>Whether presentation waits for the vertical blank.</summary>
    public static readonly BoolCVar VSync = CVar.Bool(
        "r_vsync",
        true,
        CVarOptions.Archive,
        "Wait for the vertical blank when presenting. Off uncaps the frame rate and adds tearing.");

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
        enhanced: "neutral",
        high: "neutral",

        // ACES on ultra rather than AgX. Measured over the range this game actually occupies, ACES
        // holds the most contrast of the four and AgX the least bar neutral -- AgX's strength is
        // taming very bright highlights, and a world lit by LDR lightmaps does not have any.
        ultra: "aces");

    /// <summary>The linear value that maps to display white.</summary>
    /// <remarks>
    /// How much headroom above white the tone map reserves. It must match what the scene produces:
    /// this world is LDR textures multiplied by LDR lightmaps, so almost nothing exceeds one, and the
    /// old fixed value of 4 spent the curve's whole range compressing a span that was not there. One
    /// disables the roll-off completely.
    /// </remarks>
    public static readonly NumericCVar<float> WhitePoint = CVar.Float(
        "r_white_point",
        1.1f,
        1f,
        8f,
        CVarOptions.Archive,
        "Linear value that maps to display white. Lower is more contrast; 1 disables the roll-off.");

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
        // right at a FIXED exposure -- no auto-exposure crutch. The dynamic model (ultra) lights from a GI
        // bake and a live sun whose linear values sit well below one, so a room the sun fills through
        // bounces still needs the frame lifted to read as lit; the bake-lit presets carry the artists'
        // brighter lightmaps and need less. The tone map rolls off the sky, so lifting interiors this far
        // does not blow the exterior -- measured across the outdoor, window and windowless views.
        vanilla: "1",
        enhanced: "1.4",
        high: "1.6",
        ultra: "2.5");

    /// <summary>How strongly the screen probe gather's indirect light fills the deferred lighting.</summary>
    /// <remarks>
    /// Zero is off, which is every preset for now: above zero, the gather traces the surface cache per
    /// screen probe into an atlas and the deferred lighting reads it as its diffuse fill in place of the
    /// flat ambient -- the stage where the traced global illumination reaches the screen. Not a name
    /// clash with <see cref="GlobalIllumination"/> (<c>r_gi</c>), which is the baked probe field; this is
    /// the live screen-space gather, and it needs the surface cache and the distance fields, so it does
    /// nothing without both. The magnitude reads as irradiance, so a value near the ambient it replaces
    /// (about a third) matches its brightness; it stays a knob while the look is tuned.
    /// </remarks>
    public static readonly NumericCVar<float> ScreenGather = Presets.Define(
        CVar.Float(
            "r_screen_gi",
            0f,
            0f,
            4f,
            CVarOptions.Archive,
            "Strength of the screen probe gather's indirect light in the deferred fill. 0 is off."),
        vanilla: "0",
        enhanced: "0",
        high: "0",

        // OFF at ultra now. The real-time screen gather traces only sixteen rays a probe a frame and leans
        // on the temporal filter to hide the noise, which never reads clean and cannot fill a room the way
        // the multi-bounce probe bake does. Ultra's indirect is that bake -- path-traced to convergence
        // once per mission, so it is noise-free and genuinely bright -- and the gather is left as an opt-in
        // extra for on-screen colour bleed rather than the main event. See IrradianceProbeBaker.Bounces.
        ultra: "0");

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

    /// <summary>Whether the screen probe gather traces on-screen geometry from the frame before the cache.</summary>
    /// <remarks>
    /// On by default, but only ever engaged while <see cref="ScreenGather"/> is up: each gather ray first
    /// marches the depth buffer and, on an on-screen hit, reads the previous frame's lit colour there --
    /// on-screen bounce at full frame detail rather than the surface cache's coarse card -- falling
    /// through to the cache only when the ray leaves the screen or finds nothing. A knob to turn off for
    /// comparison; it does nothing when the gather itself is off.
    /// </remarks>
    public static readonly BoolCVar ScreenGatherTrace = CVar.Bool(
        "r_screen_gi_trace",
        true,
        CVarOptions.Archive,
        "Trace on-screen geometry from the frame before falling back to the surface cache.");

    /// <summary>How far the screen probe gather's spatial denoise blur reaches, in probes. Zero is off.</summary>
    /// <remarks>
    /// The spatial half of the gather's denoise, paired with its temporal accumulation. The per-frame
    /// estimate is noisy and the temporal pass resets along depth edges, so the probes freshest near a
    /// wall shimmer; blending each probe with its neighbours, weighted so light does not cross an edge,
    /// smooths that. One probe each way is usually enough; higher is smoother but softer. Only ever runs
    /// while <see cref="ScreenGather"/> is up.
    /// </remarks>
    public static readonly NumericCVar<int> ScreenGatherFilter = CVar.Int(
        "r_screen_gi_filter",
        1,
        0,
        4,
        CVarOptions.Archive,
        "Radius, in probes, of the screen probe gather's spatial denoise. 0 is off.");

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
        enhanced: "0",
        high: "0",

        // OFF at ultra now. It was on to rescue interiors that the real-time gather left dark, but that was
        // papering over weak light: the multi-bounce GI bake and the calibrated fixed exposure (r_exposure
        // ultra 2.5) now light interiors and exteriors together WITHOUT the eye adapting, which is the
        // honest result -- a sunny scene that reads sunny at a fixed exposure rather than one the camera
        // keeps rebalancing. Left as an opt-in for anyone who wants eye adaptation.
        ultra: "0");

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

        // Ultra is the preset that stops using the artists' lightmaps. It can, because the probe field
        // is what replaces them rather than adding to them: hybrid was tried and blows the ground out,
        // since a bake that already contains the sun plus a sun is the sun twice.
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
        ultra: "2048");

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
        ultra: "320");

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
        ultra: "2.5");

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
        vanilla: "0",
        enhanced: "4",
        high: "6",
        ultra: "8");

    /// <summary>How far a surface may occlude another, in metres.</summary>
    public static readonly NumericCVar<float> AmbientOcclusionRadius = CVar.Float(
        "r_ao_radius",
        1.5f,
        0.05f,
        16f,
        CVarOptions.Archive,
        "How far, in metres, one surface can shade another.");

    /// <summary>How strongly the occlusion is applied, where 1 is the physical amount.</summary>
    public static readonly NumericCVar<float> AmbientOcclusionIntensity = CVar.Float(
        "r_ao_intensity",
        1.2f,
        0f,
        4f,
        CVarOptions.Archive,
        "How deeply ambient occlusion darkens; 1 is the physical amount.");

    /// <summary>How many samples gather bounced colour, or 0 for no screen-space bounce.</summary>
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
        ultra: "4");

    /// <summary>How strongly the bounced colour is added back.</summary>
    public static readonly NumericCVar<float> ScreenSpaceGiIntensity = CVar.Float(
        "r_ssgi_intensity",
        1f,
        0f,
        4f,
        CVarOptions.Archive,
        "How strongly surfaces bounce their colour onto each other.");

    /// <summary>How far, in metres, the camera is focused, or 0 for no depth of field.</summary>
    /// <remarks>
    /// Off by default and deliberately so: in a first-person shooter the player's eye chooses what to
    /// look at, and a renderer that blurs everything else is fighting them. Its place is the menu
    /// camera and the sniper scope, which decide where the eye is going anyway.
    /// </remarks>
    public static readonly NumericCVar<float> FocalDistance = CVar.Float(
        "r_dof",
        0f,
        0f,
        500f,
        CVarOptions.Archive,
        "Focal distance in metres; 0 turns depth of field off.");

    /// <summary>Over how many metres a surface goes from sharp to fully defocused.</summary>
    public static readonly NumericCVar<float> FocalRange = CVar.Float(
        "r_dof_range",
        12f,
        0.5f,
        200f,
        CVarOptions.Archive,
        "Metres from the focal plane over which a surface defocuses completely.");

    /// <summary>The widest the defocus gets, in pixels.</summary>
    public static readonly NumericCVar<float> FocalBlur = CVar.Float(
        "r_dof_blur",
        8f,
        0f,
        64f,
        CVarOptions.Archive,
        "The widest defocus, in pixels.");

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
        ultra: "0.3");

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
    public static readonly NumericCVar<float> SurfaceCacheDensity = CVar.Float(
        "r_cards_density",
        8f,
        1f,
        64f,
        CVarOptions.Archive | CVarOptions.RequiresRestart,
        "Texels of card per world unit of surface.");

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
    public static readonly NumericCVar<int> DistanceFieldResolution = CVar.Int(
        "r_sdf_resolution",
        32,
        8,
        128,
        CVarOptions.Archive | CVarOptions.RequiresRestart,
        "Voxels along the longest axis of each model's distance field.");

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
    public static readonly NumericCVar<float> LightScale = CVar.Float(
        "r_light_scale",
        0.7f,
        0.1f,
        2f,
        CVarOptions.Archive,
        "Scales dynamic lighting to match what the level's textures were painted for.");

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
            4f,
            CVarOptions.Archive,
            "Scales the real-time sun so sunlit surfaces read as sunlit. Only the dynamic lighting model reads it."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "2");

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
    public static readonly NumericCVar<float> SpatialAaThreshold = CVar.Float(
        "r_aa_threshold",
        0.05f,
        0.005f,
        0.5f,
        CVarOptions.Archive,
        "Luminance contrast below which a pixel is not treated as an edge.");

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
    public static readonly NumericCVar<float> BloomThreshold = CVar.Float(
        "r_bloom_threshold",
        1f,
        0.1f,
        8f,
        CVarOptions.Archive,
        "The brightness a pixel has to pass before it spills.");

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
        ultra: "1.2");

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
    public static readonly NumericCVar<float> ReflectionIntensity = CVar.Float(
        "r_ssr_intensity",
        1f,
        0f,
        2f,
        CVarOptions.Archive,
        "How strongly surfaces reflect; 1 is the physical amount.");

    /// <summary>The roughness past which a surface stops reflecting sharply enough to march for.</summary>
    public static readonly NumericCVar<float> ReflectionMaxRoughness = CVar.Float(
        "r_ssr_max_roughness",
        0.6f,
        0f,
        1f,
        CVarOptions.Archive,
        "Roughest surface that still reflects. Rougher ones scatter too widely for one ray to describe.");

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
        ultra: "1");

    /// <summary>How far apart the probes sit, in metres.</summary>
    public static readonly NumericCVar<float> GlobalIlluminationSpacing = CVar.Float(
        "r_gi_probe_spacing",
        6f,
        1f,
        32f,
        CVarOptions.Archive,
        "Metres between irradiance probes. Finer resolves smaller rooms and takes longer to bake.");

    /// <summary>How strongly the probe field's light is applied.</summary>
    public static readonly NumericCVar<float> GlobalIlluminationIntensity = CVar.Float(
        "r_gi_intensity",
        1f,
        0f,
        4f,
        CVarOptions.Archive,
        "How strongly the probe field lights surfaces; 1 is the amount the bake computed.");

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
    public static readonly NumericCVar<float> TemporalSharpening = CVar.Float(
        "r_taa_sharpen",
        0.25f,
        0f,
        1f,
        CVarOptions.Archive,
        "How much of the temporal history's resampling blur is sharpened back out.");

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
    public static readonly NumericCVar<float> FogDensity = CVar.Float(
        "r_fog_density",
        0f,
        0f,
        0.2f,
        CVarOptions.Archive,
        "Fog extinction per metre at its base height; 0 derives it from the level's own FlatSky data.");

    /// <summary>The height in metres over which fog density falls to a third.</summary>
    /// <remarks>What makes fog pool in valleys rather than hang as a flat sheet.</remarks>
    public static readonly NumericCVar<float> FogHeightFalloff = CVar.Float(
        "r_fog_height",
        24f,
        1f,
        400f,
        CVarOptions.Archive,
        "Height in metres over which fog thins out. Lower pools it harder into valleys.");

    /// <summary>How strongly the fog scatters forward.</summary>
    public static readonly NumericCVar<float> FogAnisotropy = CVar.Float(
        "r_fog_anisotropy",
        0.7f,
        -0.95f,
        0.95f,
        CVarOptions.Archive,
        "Forward scattering, -0.95 to 0.95. Positive makes fog glow towards the sun.");

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

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
