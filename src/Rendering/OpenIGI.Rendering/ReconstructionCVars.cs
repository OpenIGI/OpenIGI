using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>
/// The renderer's console variables for reconstructing the frame: render scale, the temporal resolve and
/// upscaler, and NVIDIA's DLSS, frame generation and Reflex.
/// </summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class ReconstructionCVars
{
    /// <summary>How the picture is scaled relative to the window before being presented.</summary>
    /// <remarks>
    /// Below one this is supersampling in reverse — the scene is rendered smaller and upscaled, which
    /// is the cheapest way to buy back frame time. Above one it is supersampling proper. The temporal
    /// upscaler added in stage R5 is what makes values below one look acceptable.
    /// </remarks>
    public static readonly NumericCVar<float> RenderScale = Presets.Define(
        CVar.Float(
            "r_scale",
            1.0f,
            0.5f,
            2.0f,
            CVarOptions.Archive,
            "Render resolution as a fraction of the window: below 1 is faster, above 1 is supersampled."),
        vanilla: "1",
        enhanced: "0.7",
        high: "0.85",

        // Native at ultra, and this is the ladder that separates the three real-time tiers. Everything the
        // GI stack costs -- the probe gather, the lighting, the temporal upscaler -- scales with pixel count, so
        // internal resolution is the one lever that moves all of it at once and it is therefore where the
        // tiers differ rather than in what they switch off. Ultra is native because ultra is the quality
        // tier: the temporal upscaler reconstructing from two-thirds costs about a quarter of the frame's edge energy, which
        // is a real loss to a preset whose whole job is the best picture. The cheaper tiers spend exactly
        // that, and buy back most of a traced-GI frame with it.
        //
        // The cut-out mip bias (TextureLodBias.FromSettings) is derived from this, so a tier rendering below
        // native still samples chain-link and foliage at the sharpness its output resolution wants rather
        // than discarding it. At native the bias is zero, which is correct.
        ultra: "1");

    /// <summary>How strongly the temporal pass holds on to geometry only one pixel wide.</summary>
    /// <remarks>
    /// <para>
    /// A hardware rasteriser draws a triangle only where it covers a pixel's centre, so a fence wire
    /// narrower than a pixel is drawn or skipped depending on where the frame's jitter fell. That is what
    /// makes a distant fence crawl, and what makes it fade out entirely as the wire narrows — a wire
    /// covering a third of a pixel should be a third as bright, and instead it is absent two frames in
    /// three. This lets the temporal pass keep what earlier frames saw on those pixels rather than
    /// rejecting it, so the wire is averaged rather than gambled on.
    /// </para>
    /// <para>
    /// <b>A renderer that resolves sub-pixel coverage itself has no need of this, and we switch it on
    /// deliberately.</b> A software visibility-buffer rasteriser resolves sub-pixel coverage properly, so it
    /// has no thin-geometry problem to detect. We have no such rasteriser, so the detection earns its keep
    /// here.
    /// </para>
    /// <para>
    /// Not one, at any preset. A thin thing that is genuinely moving still has to be able to reject its
    /// own history, or it drags a smear behind it — and a wire is the shape that shows a smear most.
    /// Zero restores the untreated behaviour.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> ThinGeometry = Presets.Define(
        CVar.Float(
            "r_thin_geometry",
            0.75f,
            0f,
            1f,
            CVarOptions.Archive,
            "How strongly the temporal pass holds on to sub-pixel geometry such as fence wire."),
        vanilla: "0",
        enhanced: "0.75",
        high: "0.75",
        ultra: "0.75");

    /// <summary>The temporal history's per-texel sample ceiling, which sets how much of the past it keeps.</summary>
    /// <remarks>
    /// <para>
    /// The history holds four texels per rendered pixel. A budget of sixteen samples per output pixel
    /// allows each texel four, which also zeroes the velocity weight clamp (the clamp's amplitude is
    /// <c>1 - 4 / ceiling</c>). Measured here, four is the sharpest at rest but
    /// four times as flickery on distant ridges, and without the clamp a walk ghosts; thirty-two keeps
    /// the ridge stable, is marginally sharper in motion than sixteen, and leaves the clamp holding a
    /// moving pixel to an eighth of its history.
    /// </para>
    /// <para>
    /// Not archived: the preset carries it, and a stale saved value here would silently undo a
    /// retune of the clamp it also governs.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> TemporalHistorySamples = Presets.Define(
        CVar.Float(
            "r_upscale_samples",
            32f,
            1f,
            256f,
            CVarOptions.None,
            "How many frames' worth of samples each temporal history texel may accumulate."),
        vanilla: "32",
        enhanced: "32",
        high: "32",
        ultra: "32");

    /// <summary>How many history texels the temporal upscaler keeps per rendered pixel, on each axis.</summary>
    /// <remarks>
    /// <para>
    /// Twice the render resolution is what lets an upscale reconstruct detail finer than one frame holds:
    /// the jittered frames land between each other's pixels, and a history at the rendered size would
    /// average them back down. At native resolution and above there is no finer output to reconstruct, and
    /// the doubled history is four times the pixels written and read every frame for very little -- at
    /// 2560x1440 a 5120x2880 history.
    /// </para>
    /// <para>
    /// 0 chooses: one where the frame is rendered at or above the presented size, two where it is upscaled.
    /// 1 or 2 forces that scale.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> UpscaleHistory = Presets.Define(
        CVar.Int(
            "r_upscale_history",
            0,
            0,
            2,
            CVarOptions.Archive,
            "Temporal upscaler history per rendered pixel: 0 = auto (1 at native, 2 when upscaling), 1 or 2 to force."),

        // Automatic at every tier: the render scale each preset sets already decides which one is worth it.
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0");

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

        // On at every preset above vanilla, and at the same value. It was off everywhere once because
        // soldiers are SKINNED and their motion was reported as a rigid transform -- "where the body was,
        // with the arm where it is now" -- a confidently wrong vector no feedback value could paper over.
        // The G-buffer now takes the previous clip position from the previous frame's skinned VERTICES
        // (CreateAnimatedMesh's second stream), so a walking guard's limbs reproject exactly and the smear
        // that forced this off no longer exists.
        //
        // Uniform rather than laddered, deliberately. The accumulation is what pays for the noise in every
        // stochastic pass after it -- the fog march, the horizon search, the gather ray are all
        // under-sampled on the assumption that this averages them -- and every preset above vanilla now
        // runs all of those. It is also what reconstructs detail for the tiers rendering below native, so
        // the cheapest tier needs the history most, not least. Weakening it there would take back exactly
        // what r_scale is spending. The tiers differ in resolution and ray budget; the resolve is the
        // same resolve.
        vanilla: "1",
        enhanced: "0.1",
        high: "0.1",
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
        enhanced: "0.7",
        high: "0.7",
        ultra: "0.7");

    /// <summary>What reconstructs the frame: the engine's own temporal resolve, DLSS, or DLSS ray reconstruction.</summary>
    /// <remarks>
    /// Not preset-managed: the NVIDIA kinds need the optional runtime and an adapter that runs them, and a
    /// preset that wrote one would be writing something half the machines it runs on cannot do. Where the
    /// chosen one cannot run the engine's resolve runs instead, and the log and the settings page say why.
    /// Only meaningful with temporal anti-aliasing on (<c>r_taa</c> below one) on the deferred path.
    /// </remarks>
    public static readonly EnumCVar<ReconstructionKind> Reconstruction = CVar.Choice(
        "r_reconstruction",
        ReconstructionKind.Engine,
        CVarOptions.Archive,
        "Reconstruction: engine is the built-in temporal resolve, dlss is NVIDIA DLSS / DLAA, "
            + "rayreconstruction is DLSS ray reconstruction (needs hardware ray tracing).");

    /// <summary>How far below the output DLSS renders, when DLSS or ray reconstruction is the reconstruction.</summary>
    public static readonly EnumCVar<DlssQuality> DlssQualityMode = CVar.Choice(
        "r_dlss_quality",
        DlssQuality.Quality,
        CVarOptions.Archive,
        "DLSS quality: dlaa renders at native resolution, quality/balanced/performance/ultraperformance below it. "
            + "Replaces r_scale while DLSS runs.");

    /// <summary>How many frames reach the screen per rendered one, with DLSS frame generation.</summary>
    /// <remarks>
    /// Needs an RTX 40-series or newer, hardware-accelerated GPU scheduling, and Reflex, which it turns on.
    /// A multiplier the GPU cannot reach is clamped to what it reports.
    /// </remarks>
    public static readonly EnumCVar<FrameGenerationMode> FrameGeneration = CVar.Choice(
        "r_frame_generation",
        FrameGenerationMode.Off,
        CVarOptions.Archive,
        "DLSS frame generation: off, or x2 to x6 frames on screen per rendered frame (RTX 40-series and newer).");

    /// <summary>NVIDIA Reflex: start each frame as late as the render allows, to cut input latency.</summary>
    public static readonly EnumCVar<LowLatencyMode> LowLatency = CVar.Choice(
        "r_low_latency",
        LowLatencyMode.Off,
        CVarOptions.Archive,
        "NVIDIA Reflex low latency: off, on, or boost (also keeps the GPU clocked up).");

    /// <summary>A frame-rate cap the driver enforces through Reflex, or nought for none.</summary>
    public static readonly NumericCVar<int> FrameLimit = CVar.Int(
        "r_frame_limit",
        0,
        0,
        1000,
        CVarOptions.Archive,
        "Cap the frame rate through NVIDIA Reflex, in frames per second. 0 for no cap. Needs the NVIDIA runtime.");

    /// <summary>Whether frame generation interpolates the picture and the interface separately.</summary>
    public static readonly BoolCVar FrameGenerationUiRecomposition = CVar.Bool(
        "r_dlssg_ui_recomposition",
        true,
        CVarOptions.Archive,
        "DLSS frame generation: interpolate the picture without the interface and lay the interface back over it.");

    /// <summary>Whether ray reconstruction is fed the raw traced light, or the engine's own denoised light.</summary>
    public static readonly BoolCVar RayReconstructionRaw = CVar.Bool(
        "r_rr_raw",
        true,
        CVarOptions.Developer,
        "DLSS ray reconstruction: feed it the raw one-ray-a-pixel lighting (on, as NVIDIA intends) or the engine's "
            + "own denoised lighting (off, to tell its reconstruction apart from its denoising).");

    /// <summary>Which ray-reconstruction input to show in place of its output, for checking what it was fed.</summary>
    public static readonly NumericCVar<int> RayReconstructionGuideView = CVar.Int(
        "r_rr_guide_view",
        0,
        0,
        4,
        CVarOptions.Developer,
        "Show a DLSS ray reconstruction input instead of its output: 1 diffuse albedo, 2 specular albedo, "
            + "3 normal and roughness, 4 colour before transparency. DLAA only (the input and output sizes must match).");

    /// <summary>Whether the interface is drawn into a layer of its own and composited, even with nothing needing it.</summary>
    public static readonly BoolCVar InterfaceLayer = CVar.Bool(
        "r_ui_layer",
        false,
        CVarOptions.Developer,
        "Draw the interface into its own layer and composite it over the picture even when no NVIDIA feature needs it.");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
