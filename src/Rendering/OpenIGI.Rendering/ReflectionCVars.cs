using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for reflections: the screen-space march, traced reflections and glass.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class ReflectionCVars
{
    /// <summary>How many steps the screen-space march takes, or 0 not to march at all.</summary>
    /// <remarks>
    /// The MARCH's own step count, and nothing more. It used to speak for reflections as a whole because
    /// the march was the only way they were produced; a frame that traces (see
    /// <see cref="RayTracingCVars.TracedReflections"/>) never marches, so this no longer has anything to say about it.
    /// Zero therefore means "do not march" rather than "no reflections": on a machine that traces, the
    /// reflections stand and the screen-space path is simply switched off, which is the setting this name
    /// has always implied and never meant. Whether reflections happen at all is
    /// <see cref="ReflectionIntensity"/> and <see cref="ReflectionMaxRoughness"/>.
    /// </remarks>
    public static readonly NumericCVar<int> Reflections = Presets.Define(
        CVar.Int(
            "r_ssr",
            0,
            0,
            256,
            CVarOptions.Archive,
            "Screen-space reflection: steps per marched ray, and 0 not to march. Unused on a frame that traces."),
        vanilla: "0",
        enhanced: "32",
        high: "48",
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
        enhanced: "1.7",
        high: "1.7",
        ultra: "1.7");

    /// <summary>The roughness past which a surface stops marching a screen ray and keeps the smooth fallback.</summary>
    /// <remarks>
    /// Held low deliberately. A screen-marched reflection is a SINGLE ray, so on anything but a near-mirror
    /// it reads the one surface that ray happens to hit -- for a parquet floor or a painted wall that is a
    /// dark, shadowed patch of the room, which the march then substitutes for the smooth probe fallback and
    /// the surface CRUSHES a stop or two darker than it should, besides shimmering as the ray wanders under
    /// motion. That single-ray march is what made interiors read dark and noisy. At 0.22 only genuinely
    /// glossy surfaces -- glass, a monitor, polished metal -- still march, where one ray IS the reflection;
    /// everything rougher keeps the probe field's smooth, bright, stable fallback. Without a denoised
    /// multi-ray reflection, not marching where one ray cannot help is the single-ray answer.
    /// </remarks>
    public static readonly NumericCVar<float> ReflectionMaxRoughness = Presets.Define(
        CVar.Float(
            "r_ssr_max_roughness",
            0.22f,
            0f,
            1f,
            CVarOptions.Archive,
            "Roughest surface that still marches a screen ray; rougher ones keep the smooth probe fallback."),

        // 0.5 above vanilla, and the number matters less than what stopped being true. This was 0.22 at
        // every preset while the ROUGHEST surface in the game was 0.8 and the smoothest was also 0.8 --
        // materials.json gave every texture the same value -- so the test rejected every pixel that was
        // not standing water, and the setting was a way of writing "off". It now admits the surfaces
        // materials.json names as metal, which sit at 0.45 and go below it where the detail map asks
        // for smoother.
        //
        // Measured before changing it: at 0.5, 0.18% of the office viewpoint and 0.01% of the wall
        // differ from 0.22, because with a base roughness of 0.6 there is barely anything down there
        // yet. At 0.7 -- above the base, so ordinary concrete marches -- 6.68% of the office changes and
        // its high-frequency energy FALLS slightly, so on a still frame it is not noise. That is not
        // enough to justify going there: the fault this number was lowered for was a single ray
        // wandering under MOTION, which a still frame cannot show.
        //
        // The binding constraint is no longer this value, it is the roughness floor above it.
        vanilla: "0.22",
        enhanced: "0.5",
        high: "0.5",
        ultra: "0.5");

    /// <summary>The roughest surface a traced reflection ray is fired for.</summary>
    /// <remarks>
    /// <para>
    /// The trace's own ceiling, and NOT <see cref="ReflectionMaxRoughness"/>. That one is a statement
    /// about the screen march's weakness — a marched ray reads whatever the depth buffer happens to
    /// record, so on anything but a near-mirror it substitutes one dark patch of the room for the smooth
    /// answer and the surface crushes a stop or two darker. It was set at 0.5 for that reason, and it is
    /// right for that reason. A ray has none of that failure: it returns the surface that is actually
    /// there, at whatever roughness the material claims.
    /// </para>
    /// <para>
    /// So the two are separate numbers, and this one is set by what the material file actually says. Every
    /// metal <c>materials.json</c> names sits at 0.45, and glass at 0.08; ordinary walls and floors are at
    /// 0.6 and above. 0.6 therefore admits exactly what the level calls reflective and excludes what it
    /// does not, which is the only line worth drawing here.
    /// </para>
    /// <para>
    /// There is still a ceiling at all because this is ONE ray, not a sampled lobe: past some roughness a
    /// single mirror ray is not a blurry reflection, it is a sharp reflection of the wrong thing. Raising
    /// it towards one is what a reflection denoiser would buy, and there is no reflection denoiser yet.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> TracedReflectionMaxRoughness = Presets.Define(
        CVar.Float(
            "r_rt_reflection_roughness",
            0.6f,
            0f,
            1f,
            CVarOptions.Archive,
            "Roughest surface a traced reflection ray is fired for. Separate from r_ssr_max_roughness."),
        vanilla: "0.6",
        enhanced: "0.6",
        high: "0.6",
        ultra: "0.6");

    /// <summary>The ceiling on a single traced reflection ray's radiance.</summary>
    /// <remarks>
    /// <para>
    /// A reflection is ONE ray a pixel, so nothing averages away a ray that happens to land on something
    /// brilliant -- a lamp's own emissive card, a sun-lit white wall -- and what a single unbounded ray
    /// does to a mirror is a hard bright dot that moves with the camera. Capping the read is the standard
    /// firefly guard for a single-sample estimate.
    /// </para>
    /// <para>
    /// Separate from <c>r_screen_gi_max</c>, which looks like the same number and is not: that one bounds
    /// a loop -- the gather reads the lit scene and the lit scene is then built from the gather -- and is
    /// set by where that loop stops running away. This bounds a single sample's variance and has no
    /// feedback in it at all, so tying the two would mean a change made for one reason silently landing
    /// on the other.
    /// </para>
    /// <para>
    /// High enough to leave an ordinary reflected wall or sky untouched, which is the whole intent: this
    /// should clip the outliers and nothing else. Zero disables the trace's contribution outright, which
    /// is a diagnostic rather than a setting.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> TracedReflectionClamp = Presets.Define(
        CVar.Float(
            "r_rt_reflection_clamp",
            50f,
            0f,
            1000f,
            CVarOptions.Archive,
            "Ceiling on one traced reflection ray's radiance; stops a bright hit reading as a firefly."),
        vanilla: "50",
        enhanced: "50",
        high: "50",
        ultra: "50");

    /// <summary>How many texels square each texture is stored at for shading a traced hit.</summary>
    /// <remarks>
    /// <para>
    /// A reflected ray that strikes something the camera cannot see — a body, most of all — is shaded
    /// from a reduced copy of that surface's own texture. This is how big those copies are, and it is the
    /// only thing standing between a reflected character and the source art.
    /// </para>
    /// <para>
    /// <b>The cost is quadratic and it is real.</b> The array holds a thousand textures so that a level's
    /// pack, its interface and every character fit; at 128 that is 67 MB of video memory and at 256 it is
    /// 268 MB. There is no middle setting that is free. 128 is enough for a reflection glimpsed across a
    /// courtyard and visibly short of it for your own face in a window at arm's length, which is the shot
    /// people actually look at.
    /// </para>
    /// <para>
    /// Read once, when the device is created: the array is allocated at start-up and this cannot resize
    /// it. Change it and restart.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<int> ReflectionTexture = Presets.Define(
        CVar.Int(
            "r_reflection_texture",
            128,
            64,
            256,
            CVarOptions.Archive,
            "Texels square per texture in the traced reflection's albedo array (67 MB at 128, 268 MB at 256)."),
        vanilla: "64",
        enhanced: "128",
        high: "128",
        ultra: "256");

    /// <summary>How much of what a window pane reflects reaches the picture.</summary>
    /// <remarks>
    /// <para>
    /// A multiplier on the Fresnel share, and it exists because the honest number is unusably small. Glass
    /// seen square-on reflects four per cent, which is correct physics and almost invisible against a lit
    /// interior: the reflection is computed, it is in the frame, and nobody can see it. Every game that
    /// shows glass reflecting anything at a normal viewing angle is exaggerating, and this is the dial that
    /// says by how much.
    /// </para>
    /// <para>
    /// It multiplies the Fresnel term rather than replacing it, so the ANGULAR behaviour is untouched: a
    /// pane still goes from barely-there head-on to a mirror at a glancing angle, which is what makes
    /// glazing read as glazing. What changes is only where on that curve the useful range sits. One is the
    /// physical answer; past about four a pane starts to read as polished metal rather than glass.
    /// </para>
    /// <para>
    /// Use <c>r_debug_view glass</c> to see what is being weighted: it draws the panes' reflection raw,
    /// with this and the Fresnel share both taken off.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> GlassReflection = Presets.Define(
        CVar.Float(
            "r_glass_reflection",
            1f,
            0f,
            8f,
            CVarOptions.Archive,
            "How much of a window pane's reflection reaches the picture; multiplies the Fresnel share."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
