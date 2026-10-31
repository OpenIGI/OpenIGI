using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>
/// The renderer's console variables for how a frame is made and shown: presentation, the scene path, what
/// is drawn of the player, and the diagnostics laid over it.
/// </summary>
/// <remarks>
/// <para>
/// The renderer's variables are declared next to the renderer seam, rather than in a central settings type,
/// and grouped by what they govern: this class, and its siblings <see cref="ExposureCVars"/>,
/// <see cref="LightingCVars"/>, <see cref="ShadowCVars"/>, <see cref="PostCVars"/> and the rest. The console,
/// the settings file and the options menu all reach them through <see cref="CVarRegistry.Default"/>, so there
/// is one value and three views onto it.
/// </para>
/// <para>
/// Each remaster stage adds its own variables to the container for what they govern and, where the value
/// differs by quality tier, registers them with <see cref="Core.Configuration.Presets.GraphicsPresetRegistry"/>
/// in the same place.
/// </para>
/// </remarks>
[CVarContainer]
public static class FrameCVars
{
    /// <summary>Whether presentation waits for the vertical blank.</summary>
    /// <remarks>On under every preset, so choosing one brings back a tear-free picture whatever was saved.</remarks>
    public static readonly BoolCVar VSync = Presets.Define(
        CVar.Bool(
            "r_vsync",
            true,
            CVarOptions.Archive,
            "Wait for the vertical blank when presenting. Off uncaps the frame rate and adds tearing."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether models are drawn at the original's levels of detail, coarser with distance, rather than always whole.</summary>
    /// <remarks>
    /// Off under every preset, the original's own look included: every model is drawn at its finest level at any distance.
    /// Not archived, as <see cref="MenuBackdropLite"/> is not: it is for comparing with the original, not a preference.
    /// </remarks>
    public static readonly BoolCVar ModelLod = CVar.Bool(
        "r_model_lod",
        false,
        CVarOptions.None,
        "Draw models at the original's levels of detail, coarser with distance. Off draws every model at its finest.");

    /// <summary>Whether the level behind the menu is drawn without the expensive parts of the preset.</summary>
    /// <remarks>
    /// The backdrop is looked at through a blur, never played, and it is replaced every time the player
    /// settles on another mission. Building its real-time lighting -- the surface-cache capture and the
    /// distance field -- and tracing its shadows and reflections made every swap a stall the player saw,
    /// for detail nobody could. On, the menu draws it on the level's own bakes with no shadows, traces or
    /// screen-space gathers; the full preset comes back when Play is pressed, behind the going-in sequence.
    /// Imposed through overrides, so the settings screen and the saved file still show the player's choice.
    /// </remarks>
    public static readonly BoolCVar MenuBackdropLite = CVar.Bool(
        "r_menu_lite",
        true,

        // Not archived: a switch for comparing, as r_rt is, not a preference -- a session that turned it
        // off to look must not start that way tomorrow.
        CVarOptions.None,
        "Draw the level behind the menu without real-time GI, shadows or traces, so swapping it never stalls.");

    /// <summary>Whether the presentation interpolates between simulation ticks.</summary>
    /// <remarks>
    /// <para>
    /// The simulation is fixed at 30 Hz and stays there — that is the determinism contract. What this
    /// changes is how each frame renders bodies, poses and the player's
    /// own eye blended between the last two tick states, so an uncapped frame rate looks uncapped
    /// instead of a 30 Hz slideshow with a smooth mouse. AI and ballistics keep reading tick states.
    /// IGI2 also uses linear mouse counts for both the frame preview and the next tick's aim, bypassing
    /// its retail pitch filters and turn cap. Off retains that title's retail mouse response.
    /// </para>
    /// <para>
    /// On under vanilla too. The original presents exactly one tick's state per frame and vanilla is
    /// otherwise held to what the original did, but a 30 Hz slideshow is not a look anyone chose to
    /// preserve -- it is what a 1998 target frame rate left behind, and the tier exists to show the
    /// original's <em>picture</em>, not its schedule. Reference captures use <c>-lockstep</c>, which
    /// disables interpolation and unfiltered mouse aim ahead of this setting.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar SmoothPresentation = Presets.Define(
        CVar.Bool(
            "r_smooth",
            true,
            CVarOptions.Archive,
            "Smooth movement between ticks and use unfiltered IGI2 mouse aim. Simulation stays 30 Hz."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

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

    /// <summary>How many frames a level is drawn for, unseen, before it is shown to the player.</summary>
    /// <remarks>
    /// <para>
    /// Most of this renderer's lighting is defined in terms of frames rather than of state: the screen
    /// probe gather accumulates per pixel, the radiance cache fills its probes, the temporal resolve
    /// needs a history before it can reconstruct anything, and the eye adapts towards a scene instead of
    /// snapping to it. A load cannot produce any of that, however long it takes -- there is no history
    /// without frames -- so a level that is shown the instant it finishes loading is shown mid-converge,
    /// and the player watches it settle over the next second or two.
    /// </para>
    /// <para>
    /// This draws those frames with the picture withheld. The scene renders exactly as it will in play,
    /// so every temporal pass advances normally; what is held back is only the presentation. It is what a
    /// loading screen is actually for, and it is why no shipping game shows its world changing after the
    /// loading screen has gone.
    /// </para>
    /// <para>
    /// Forty-five, measured rather than picked: with <see cref="SurfaceCacheCVars.GiConverge"/> doing the view-independent
    /// half at load, frame twenty is about 7% off the settled image and frame forty-five about 3%, which
    /// is past telling apart. Zero shows the level the moment it loads, which is the old behaviour.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> MissionWarmUp = Presets.Define(
        CVar.Int(
            "r_mission_warmup",
            45,
            0,
            240,
            CVarOptions.Archive,
            "Frames a level is drawn for, unseen, before it is revealed, so it appears settled."),

        // Vanilla has no temporal pass to warm: it draws the frame the original drew and presents it.
        vanilla: "0",
        enhanced: "45",
        high: "45",
        ultra: "45");

    /// <summary>How many samples per pixel the scene is rasterised with.</summary>
    /// <remarks>
    /// Resolved down before anything samples the target, because the effects that follow read depth
    /// and a multisampled depth buffer is not something a shader can read usefully.
    /// <para>
    /// It applies ONLY on the original's forward path (<c>r_hdr 0</c>): the deferred/HDR path records into
    /// a single-sample G-buffer and is anti-aliased by the temporal upscaler instead, so MSAA there would be pure cost for no
    /// effect. Every preset from enhanced up is deferred, so they set it to off — a value above one on
    /// those presets did nothing but read as if it were working.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> MultiSampling = Presets.Define(
        CVar.Int(
            "r_msaa",
            4,
            0,
            8,
            CVarOptions.Archive,
            "Samples per pixel on the forward (r_hdr 0) path; off on the deferred path, where the temporal upscaler is the AA."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the player's own body is drawn while the camera is in first person.</summary>
    /// <remarks>
    /// <para>
    /// The body already exists and is already animated — third person draws it, and so does the death
    /// camera. Two gates hold it shut in first person: the follow that places it, and the draw latch the
    /// third-person camera keeps closed until it has left the head. This opens both.
    /// </para>
    /// <para>
    /// <b>It does NOT put the player in a window, and it is worth being exact about why.</b> A traced
    /// reflection resolves its hit out of the lit frame first and the static surface cache second. Facing
    /// a window, the player's own body is BEHIND the camera — so the hit fails the first test at the very
    /// first line, on being behind the eye, and the cards never held a moving body to begin with. Drawing
    /// it changes nothing about that. What it does reach is the cases where the body is partly in frame
    /// anyway: a pane off to one side, or a glancing angle along a glass wall.
    /// </para>
    /// <para>
    /// What it buys unconditionally is the player's own shadow and its ambient occlusion, which the
    /// original never had, and the body being visible on looking down.
    /// </para>
    /// <para>
    /// <b>Off by default, and that is not timidity.</b> The camera sits at the head, so the head is either
    /// clipped by the near plane or seen from the inside — the skull filling the screen that the draw
    /// latch exists to prevent. Hiding the head bone is the usual answer and is not done here yet, so
    /// switching this on today trades a known artefact for a benefit most players will not see.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar PlayerBody = Presets.Define(
        CVar.Bool(
            "r_player_body",
            false,
            CVarOptions.Archive,
            "Draw the player's body in first person, so it casts shadows and can appear in reflections."),
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0");

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

    /// <summary>How many of the most expensive individual passes r_perflog lists under its stages.</summary>
    /// <remarks>
    /// A stage is the sum of its passes, and the global illumination alone is a dozen dispatches; a stage
    /// over budget says where to look but not at what. Every pass is also written to the log on exit.
    /// </remarks>
    public static readonly NumericCVar<int> PerformanceLogPasses = CVar.Int(
        "r_perflog_passes",
        0,
        0,
        32,
        CVarOptions.Developer,
        "With r_perflog, list this many of the most expensive individual GPU passes, by name.");

    /// <summary>Which buffer to show instead of the scene.</summary>
    public static readonly EnumCVar<DebugView> Debug = CVar.Choice(
        "r_debug_view",
        DebugView.None,
        CVarOptions.Developer,
        "Show a render buffer instead of the scene: none, albedo, normal, depth, material, motion, bakedlight, wetness, roughness, metallic, materialao.");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
