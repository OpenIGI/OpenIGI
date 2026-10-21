using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for fog: the original's distance fog and the volumetric fog.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class FogCVars
{
    /// <summary>The anisotropy of the fog's second, backward-scattering lobe.</summary>
    /// <remarks>
    /// Haze scatters mostly forward, but not only: with one forward lobe a shaft of light shows only looking
    /// into the sun, and vanishes seen from the side. A weak backward lobe keeps it.
    /// </remarks>
    public static readonly NumericCVar<float> FogBackScatter = Presets.Define(
        CVar.Float(
            "r_fog_back_scatter",
            -0.25f,
            -0.95f,
            0f,
            CVarOptions.Archive,
            "Anisotropy of the fog's backward lobe; what keeps a light shaft visible from the side."),
        vanilla: "-0.25",
        enhanced: "-0.25",
        high: "-0.25",
        ultra: "-0.25");

    /// <summary>How much of the fog's phase the backward lobe carries.</summary>
    public static readonly NumericCVar<float> FogBackScatterWeight = Presets.Define(
        CVar.Float(
            "r_fog_back_weight",
            0.25f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much of the fog's scattering goes into its backward lobe, 0 to 1."),
        vanilla: "0.25",
        enhanced: "0.25",
        high: "0.25",
        ultra: "0.25");

    /// <summary>How much the fog's density varies through the air.</summary>
    /// <remarks>An even medium lights an even shaft, which reads as a pane of light; the variation gives it body.</remarks>
    public static readonly NumericCVar<float> FogNoise = Presets.Define(
        CVar.Float(
            "r_fog_noise",
            0.5f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much the fog's density varies, drifting with the wind; 0 is an even medium."),
        vanilla: "0",
        enhanced: "0.3",
        high: "0.5",
        ultra: "0.5");

    /// <summary>Whether the world wears the original's own per-vertex distance fog.</summary>
    /// <remarks>
    /// <para>
    /// The retail picture is a fogged one. Every shipping level's <c>FlatSky</c> asks for a fog amount
    /// between 0.70 and 0.975, which is half of a surface gone by four hundred metres and nearly all of
    /// it by two and a half kilometres, and the same slate blue to fog into. Without it the ground runs
    /// to the horizon in full colour and the mountain ranges of missions 5, 9 and 11 stand up sharp
    /// behind the level -- which is not what the original draws, whatever the far plane is.
    /// </para>
    /// <para>
    /// On for vanilla and off above it, because the modern tiers already integrate the same level data
    /// through <see cref="FogQuality"/>'s froxel volume, which scatters and shadows it rather than
    /// lerping towards a constant. Turning both on would fog the frame twice. See
    /// <see cref="SceneFog"/> for the arithmetic and where it was read from.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar RetailFog = Presets.Define(
        CVar.Bool(
            "r_fog_retail",
            false,
            CVarOptions.Archive,
            "Fog the world the way the original does: exp(-density * view depth) into the level's own fog colour."),
        vanilla: "1",
        enhanced: "0",
        high: "0",
        ultra: "0");

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

        // Ultra takes the level's own density like every other tier. It used to override it with 0.11,
        // which is an extinction that leaves five per cent of a surface visible at twenty-seven metres --
        // the whole frame a white wash. That went unnoticed because no backend rendered the medium at the
        // time it was set: the OpenGL path that could have was removed, and the Vulkan one only reached
        // its setter. A level's own FlatSky haze is both authored and per-mission, which is the right
        // default now that the froxel volume actually integrates it.
        vanilla: "0",
        enhanced: "0",
        high: "0",
        ultra: "0");

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

        // Pinned to the default on every tier, like the other three fog dials. These describe the SHAPE
        // of a mission's atmosphere, which fog-presets.json authors per level; a quality tier has no
        // business replacing a level's own air with one number, and the shell reads "the player moved
        // this off its default" as the signal to override, so a tier that assigned a value would be
        // indistinguishable from a player who had.
        vanilla: "24",
        enhanced: "24",
        high: "24",
        ultra: "24");

    /// <summary>How strongly the fog scatters forward.</summary>
    public static readonly NumericCVar<float> FogAnisotropy = Presets.Define(
        CVar.Float(
            "r_fog_anisotropy",
            0.7f,
            -0.95f,
            0.95f,
            CVarOptions.Archive,
            "Forward scattering, -0.95 to 0.95. Positive makes fog glow towards the sun."),

        // Pinned to the default on every tier, like the other three fog dials. These describe the SHAPE
        // of a mission's atmosphere, which fog-presets.json authors per level; a quality tier has no
        // business replacing a level's own air with one number, and the shell reads "the player moved
        // this off its default" as the signal to override, so a tier that assigned a value would be
        // indistinguishable from a player who had.
        vanilla: "0.7",
        enhanced: "0.7",
        high: "0.7",
        ultra: "0.7");

    /// <summary>How far, in metres, the fog is marched before the cheaper haze takes over.</summary>
    public static readonly NumericCVar<float> FogDistance = Presets.Define(
        CVar.Float(
            "r_fog_distance",
            500f,
            50f,
            2000f,
            CVarOptions.Archive,
            "How far volumetric fog is marched, in metres."),

        // Pinned to the default on every tier, like the other three fog dials. These describe the SHAPE
        // of a mission's atmosphere, which fog-presets.json authors per level; a quality tier has no
        // business replacing a level's own air with one number, and the shell reads "the player moved
        // this off its default" as the signal to override, so a tier that assigned a value would be
        // indistinguishable from a player who had.
        // Distance used to be tiered because a longer march cost more steps. It does not any more: the
        // froxel volume has the same cell count whatever range it covers, so how far the fog reaches is
        // a look rather than a price, and it belongs with the rest of the mission's atmosphere.
        vanilla: "500",
        enhanced: "500",
        high: "500",
        ultra: "500");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
