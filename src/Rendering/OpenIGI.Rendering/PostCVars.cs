using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the finished frame: anti-aliasing, bloom, the grade, lens effects and sharpening.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class PostCVars
{
    /// <summary>How far dim parts of a night picture shift towards night vision, 0 to 1.</summary>
    /// <remarks>
    /// The Purkinje shift: see <c>PostProcessSettings.NightShift</c>. Scaled by how far into night the sky
    /// is, so a day is untouched whatever this is set to.
    /// </remarks>
    public static readonly NumericCVar<float> NightShift = Presets.Define(
        CVar.Float(
            "r_night_shift",
            0f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much a night's dim surfaces lose their colour to a cool night-vision blue. 0 is off."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
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
        ultra: "0.5");

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
        enhanced: "0",
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
        enhanced: "0.33",
        high: "0.33",
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
        ultra: "0.2");

    /// <summary>The luminance above which a pixel starts to bloom, or negative to bloom everything.</summary>
    /// <remarks>
    /// <para>
    /// Ultra does not threshold at all: every pixel spills in proportion to its brightness rather than
    /// only those above a cut. That is what makes the bloom read as lens scatter instead of as a glow
    /// that switches on -- a hard cut shows as a contour where a surface crosses it.
    /// </para>
    /// <para>
    /// Zero is that "do not threshold" case rather than a negative sentinel such as minus one, because a
    /// console value beginning with a minus cannot be typed: the argument parser reads it as the next
    /// option, so `+r_bloom_threshold -1` exits rather than setting anything. A value nobody can type is
    /// not a control. Zero is unambiguous because a threshold of zero and no threshold at all differ --
    /// the first still scales dim pixels down towards nothing, the second passes them through.
    ///
    /// The lower bound had to open to admit it: the range was 0.1 to 8, which cannot express the case at
    /// all, so ultra carried 2.47 -- a number the backend ignored, because the bloom chain was written
    /// unthresholded directly. The value now says what the picture actually
    /// does. Vanilla keeps a real threshold, having no tone curve for this to be calibrated against.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> BloomThreshold = Presets.Define(
        CVar.Float(
            "r_bloom_threshold",
            1f,
            0f,
            8f,
            CVarOptions.Archive,
            "The brightness a pixel has to pass before it spills; 0 spills everything."),
        vanilla: "1",
        enhanced: "0",
        high: "0",
        ultra: "0");

    /// <summary>How far colour is pushed away from mid-grey.</summary>
    /// <remarks> Ultra sits at 1, which is neutral, because its target look grades to identity — contrast,
    /// gamma and gain all 1, offset 0, and no lookup table. It had been carrying a value that came in with
    /// a preset seeded from a saved config, and tinting a picture that is meant to be untinted only ever
    /// moves it away. Enhanced and high keep a gentle grade of their own; it is taste rather than fidelity,
    /// and the settings menu offers all four as one Image Style choice.
    /// </remarks>
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
        ultra: "1");

    /// <summary>How far colour is pushed away from its own luminance.</summary>
    /// <remarks> Ultra sits at 1, which is neutral, because its target look grades to identity —
    /// saturation, gamma and gain all 1, offset 0, and no lookup table. It had been carrying a value that
    /// came in with a preset seeded from a saved config, and tinting a picture that is meant to be untinted
    /// only ever moves it away. Enhanced and high keep a gentle grade of their own; it is taste rather than
    /// fidelity, and the settings menu offers all four as one Image Style choice.
    /// </remarks>
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
        ultra: "1");

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
        ultra: "0.4");

    /// <summary>How much film grain is added.</summary>
    public static readonly NumericCVar<float> Grain = Presets.Define(
        CVar.Float(
            "r_grain",
            0f,
            0f,
            0.2f,
            CVarOptions.Archive,
            "Film grain, an artistic effect. Banding is handled by the resolve's own one-step dither."),

        // Off on every preset. It was 0.03 to break up an 8-bit display's banding, which is a dither's job
        // and not a grain's: at that strength it moved nearly four code values either way every frame,
        // and at night -- where most of a frame is a few code values above black -- it changed nearly
        // half the pixels of one frame to the next and read as noise over every shadow. The resolve now
        // dithers by one code value, which removes the banding without the noise.
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

        // Off at ultra. The ultra look has no lens fringing, and against a window's high-contrast edges the
        // aberration read as coloured fringes that made the frame look less clean, not more filmic. High
        // keeps a little, since it is not held to that look.
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

        // Off: ultra grades to identity. The mission's table lifts black to a cool grey and pulls every
        // shadow towards blue, which is the opposite of the deep, warm darks ultra is after.
        ultra: "0");

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
    /// <remarks>
    /// Ultra takes 0. That is not declining a free improvement: sharpening is a correction for a soft
    /// resolve, and ultra runs its temporal reconstruction at native resolution, where there is no
    /// upsampling softness to correct.
    /// <para>
    /// Enhanced and high keep theirs, and the reason is now <see cref="ReconstructionCVars.RenderScale"/> rather than a cruder
    /// resolve — they run the identical reconstruction, but from 70% and 85% of the output pixels, and
    /// upsampling is exactly the soft resolve this corrects. The ladder follows the deficit: the further
    /// below native a tier renders, the more of this it wants.
    /// </para>
    /// </remarks>
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
        ultra: "0");

    /// <summary>How strongly light shafts radiate from the sun when it is on screen.</summary>
    /// <remarks>
    /// The sky round the sun blurred outward in screen space, so a lattice, a fence or a roof edge between
    /// the eye and the sun cuts rays into its glow. The volume's own shafts need air thick enough to show
    /// them; a clear morning has none, and still shows these.
    /// </remarks>
    public static readonly NumericCVar<float> SunShafts = Presets.Define(
        CVar.Float(
            "r_sun_shafts",
            1f,
            0f,
            2f,
            CVarOptions.Archive,
            "How strongly light shafts radiate from the sun when it is on screen; 0 is off."),
        vanilla: "0",
        enhanced: "0.5",
        high: "0.8",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
