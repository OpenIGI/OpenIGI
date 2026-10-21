using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the sky: its light, the atmosphere, and the night sky and moon.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class SkyCVars
{
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

            // Three times the captured sky, which puts the window light of an interior on the scale the
            // tuned exposure expects.
            3f,
            0f,
            16f,
            CVarOptions.Archive,
            "Skylight brightness the GI fill sees; 1 is the level's own ambient. Lifts outdoor and window-lit surfaces."),

        // One -- the sky as the atmosphere scatters it -- on every preset that gathers it. Three times that
        // made the fill in every shadow three times what the sky above it gives, and the sun barely read
        // against it: shaded ground a stop and a half too bright and blue, the walls the sun fell on flat.
        // The exposure adapts to the darker fill, as an eye does. The vanilla preset keeps its own.
        vanilla: "3",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Scales the sun that generates the atmospheric sky, independently of direct light.</summary>
    /// <remarks>
    /// The atmosphere produces both the visible sky and the diffuse SH that lights surfaces. It used to
    /// read <see cref="LightingCVars.SunScale"/>, which meant calibrating direct sunlight silently scaled the sky by the
    /// same amount and could never change their balance. Keeping the old preset values here preserves the
    /// measured sky while <see cref="LightingCVars.SunScale"/> is corrected independently.
    /// </remarks>
    public static readonly NumericCVar<float> AtmosphereSunScale = Presets.Define(
        CVar.Float(
            "r_atmosphere_sun_scale",
            1f,
            0f,
            16f,
            CVarOptions.Archive,
            "Sun source intensity used to generate the atmospheric sky and its diffuse irradiance."),
        vanilla: "1",
        enhanced: "7",
        high: "7",

        // Preserve the atmosphere and sky SH that ultra was already rendering when r_sun_scale was 7.
        // This is a transport input, not the direct surface-response calibration in r_sun_scale.
        ultra: "7");

    /// <summary>How much of the moon's light the atmosphere scatters into the night sky.</summary>
    /// <remarks>
    /// <para>
    /// A moonlit sky is the daytime sky's own blue, lit by a source some four hundred thousand times
    /// dimmer. Handed the moon at face value the march drew what the eye reads as a dim DAY, so it was
    /// knocked down to two percent. That left the sky and the harmonics projected from it black, and
    /// with them every shadow the moon did not reach: nothing lit a night but the moon's direct light
    /// and the lamps.
    /// </para>
    /// <para>
    /// What made the moonlit blue read as day was the missing night vision, not the blue. With the
    /// resolve's night shift taking the colour out of the dim sky, a third of the moon lets the sky light
    /// the world again and still reads as night.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> NightSkyScale = Presets.Define(
        CVar.Float(
            "r_night_sky",
            0.02f,
            0f,
            1f,
            CVarOptions.Archive,
            "How much of the moon's light the night sky scatters, which is what fills a night's shadows."),
        vanilla: "0.02",
        enhanced: "0.35",
        high: "0.35",
        ultra: "0.35");

    /// <summary>The night sky's own glow at the zenith, in the atmosphere's units.</summary>
    /// <remarks>
    /// Airglow: the upper atmosphere's faint emission, which is most of a clear moonless sky's light. It
    /// is what keeps a night without a moon from being black, and it brightens towards the horizon, where
    /// the eye looks through more of the glowing layer. Nought turns it off.
    /// <para>
    /// On for every preset, faintly. Once the exposure meters the whole frame, the night is metered off
    /// the sky and the ground together, and without the glow a clear night had almost nothing in it to
    /// meter: a black frame with stars. The glow is the moonless night's own light, so it is what that
    /// frame should be showing -- at 0.0005, a dark night; 0.004 reads as a bright one.
    /// </para>
    /// </remarks>
    public static readonly NumericCVar<float> NightAirglow = Presets.Define(
        CVar.Float(
            "r_night_airglow",
            0.0005f,
            0f,
            1f,
            CVarOptions.Archive,
            "The night sky's own faint glow, which lights a moonless night. 0 is off."),
        vanilla: "0.0005",
        enhanced: "0.0005",
        high: "0.0005",
        ultra: "0.0005");

    /// <summary>Whether the night sky draws its stars, the galaxy and the moon's disc.</summary>
    public static readonly BoolCVar Stars = Presets.Define(
        CVar.Bool(
            "r_stars",
            false,
            CVarOptions.Archive,
            "Draw the stars, the Milky Way and the moon's disc in a night sky."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How strongly the moon lights the world, against the cycle's own moon; 0.1 by default.</summary>
    /// <remarks>
    /// Scales the moon's light -- what throws moon shadows and lights the ground -- and with it the
    /// moonlit sky, which is scattered from the same moon, as a dimmer moon makes a darker sky. The disc
    /// keeps its own dial (<see cref="MoonBrightness"/>), and the airglow that lights a moonless night is
    /// not the moon's at all.
    /// </remarks>
    public static readonly NumericCVar<float> Moonlight = Presets.Define(
        CVar.Float(
            "r_moonlight",
            0.1f,
            0f,
            4f,
            CVarOptions.Archive,
            "How strongly the moon lights the world; 0 leaves only the night sky's fill."),
        vanilla: "0.1",
        enhanced: "0.1",
        high: "0.1",
        ultra: "0.1");

    /// <summary>How bright the moon's disc and halo are drawn in a night sky; one is the default.</summary>
    /// <remarks>
    /// The disc only: the moonlight on the world is the directional light and is not touched by this.
    /// </remarks>
    public static readonly NumericCVar<float> MoonBrightness = Presets.Define(
        CVar.Float(
            "r_moon_brightness",
            1f,
            0f,
            4f,
            CVarOptions.Archive,
            "How bright the moon's disc and halo are drawn; 0 hides it. Does not change the moonlight."),
        vanilla: "1",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
