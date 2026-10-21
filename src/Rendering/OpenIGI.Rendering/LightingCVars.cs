using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the lighting model: where surfaces take their light from, and its calibration.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class LightingCVars
{
    /// <summary>Whether surfaces are recorded into a G-buffer and lit afterwards.</summary>
    /// <remarks>
    /// Required by everything from stage R3 on. Kept separate from <see cref="FrameCVars.OffscreenScene"/> so the
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
    /// This waited on <c>baked</c> for every preset until the sun of stage R3 and the probe irradiance of
    /// R7 existed, because promoting it before then would have traded hand-authored lighting for a flat
    /// ambient term. Both exist now, and every preset above vanilla has been promoted — see the note on the
    /// rows below for what carries the surfaces the screen gather cannot resolve. Vanilla stays
    /// <c>baked</c> for the same reason it stays on the forward path: it is the regression gate, and it has
    /// to keep reproducing the picture the game shipped with.
    /// </remarks>
    public static readonly EnumCVar<LightingModel> Lighting = Presets.Define(
        CVar.Choice(
            "r_lighting",
            LightingModel.Baked,
            CVarOptions.Archive,
            "Light source: baked reproduces the original, hybrid adds the sun, dynamic ignores the bakes."),
        vanilla: "baked",
        enhanced: "dynamic",
        high: "dynamic",

        // Enhanced, high and ultra are all real-time presets now, and they light identically: from the
        // screen probe gather (r_screen_gi) over the world-space radiance cache (r_radiance_cache), with the
        // level's own fixtures (r_local_light_scale) lighting the windowless rooms the gather has no source
        // for. What separates the three is budget alone -- internal resolution, how widely the relight is
        // spread, how often the gather re-traces -- and never which systems are running. That is the point
        // of the arrangement: a machine that cannot afford ultra gets a cheaper version of the same picture
        // rather than a different one.
        //
        // The baked probe volume (r_gi) is off at all three, so a surface none of the real-time systems can
        // resolve falls back to the flat ambient term rather than to a traced estimate. See the note on
        // r_gi: restoring that floor is a live question, and it applies to all three equally now.
        ultra: "dynamic");

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
            // on seven, the illuminance the rest of the ultra calibration assumes, so any remaining error
            // is the transport's fault, not the key light's. Ultra carried a hand-set 0.96 for a while,
            // about a tenth of a stop over; measured across five regions it was worth about half a level
            // of error, and the derivation is the better authority.
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

    /// <summary>Calibrates the real-time sun against the sky fill.</summary>
    /// <remarks>
    /// Only the dynamic model reads this; the baked model lights from its lightmaps and never adds a live
    /// sun. The atmospheric source has its own independently normalised control so changing this value
    /// does not move the sky with it.
    /// </remarks>
    public static readonly NumericCVar<float> SunScale = Presets.Define(
        CVar.Float(
            "r_sun_scale",
            1f,
            0.25f,
            16f,
            CVarOptions.Archive,
            "Calibrates the real-time sun against the sky fill. Only the dynamic lighting model reads it."),
        vanilla: "1",
        enhanced: "7",
        high: "7",

        // Restored to 7. The 1.51 before it came from a fixed-exposure sample that read our sun as
        // 1.714 times our sky and divided the sun down to a target ratio; the sample points were a wall
        // and its shade, not a sunlit floor and its shadow, and the target they implied put the sun
        // BELOW its own fill, which no sunlit exterior does. Measured again over 1056 office-floor
        // points at the same pose, the ninetieth percentile -- the same surface with the sun on it --
        // lands within one per cent of the expected value, and so does the sun's own share of it. The
        // sun was never the term that was wrong; the bounce under it was, and r_gi_bounce carries that
        // correction.
        //
        // Dividing it by 4.6 took the whole scene down with it, which is what made this visible: the
        // frame's average luminance fell under AutoExposurePass's metering floor, the adapted exposure
        // pinned against its ceiling of ten and could open no further, and every shadow slid into the
        // tone curve's toe. That is the "shadows read as a solid slab" report.
        ultra: "7");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
