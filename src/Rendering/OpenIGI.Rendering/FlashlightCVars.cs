using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the player's flashlight: its beam, its light and how it is carried.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class FlashlightCVars
{
    /// <summary>Whether the player's flashlight can be switched on.</summary>
    /// <remarks>
    /// <para>
    /// <b>Not retail.</b> The original has no flashlight; this is the remaster's, toggled by the Flashlight
    /// action (F by default). Vanilla pins it off, and there the key does nothing at all -- no light and no
    /// click -- so a vanilla run is the original and nothing else. The light also needs the deferred path,
    /// which vanilla does not draw, so a Custom preset that switches that off switches this off with it.
    /// </para>
    /// <para>
    /// It works the same with the RT switches on or off: its shadow is traced against the world where the
    /// adapter can and marched through the depth buffer where it cannot (<see cref="FlashlightShadows"/>).
    /// </para>
    /// </remarks>
    public static readonly BoolCVar Flashlight = Presets.Define(
        CVar.Bool(
            "r_flashlight",
            true,
            CVarOptions.Archive,
            "Let the Flashlight key (F) switch on the player's flashlight. Not in the original."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the flashlight casts shadows.</summary>
    /// <remarks>
    /// One ray a pixel from each lit surface to a point on the lens, then a small filter sized by the
    /// penumbra; traced against the world, cut-out masks and all, where the RT switches allow, and marched
    /// through the depth buffer everywhere else. Nothing runs while the light is off.
    /// </remarks>
    public static readonly NumericCVar<int> FlashlightShadows = Presets.Define(
        CVar.Int(
            "r_flashlight_shadows",
            1,
            0,
            1,
            CVarOptions.Archive,
            "Let the flashlight cast shadows."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>How strongly the flashlight's beam shows in the air.</summary>
    /// <remarks>
    /// Scales what the beam scatters out of the volumetric fog, plus the thin dust only it sees
    /// (<see cref="FlashlightDust"/>). Needs the volumetric fog; with none there is no beam in the air.
    /// </remarks>
    public static readonly NumericCVar<float> FlashlightFog = Presets.Define(
        CVar.Float(
            "r_flashlight_fog",
            0.35f,
            0f,
            4f,
            CVarOptions.Archive,
            "How strongly the flashlight's beam shows in the air. 0 hides it."),
        vanilla: "0",

        // A third of the physical answer. In a level with real fog the full backscatter is the wall of white
        // a car's high beams make in fog -- true, and exactly what nobody wants between them and the room.
        enhanced: "0.35",
        high: "0.35",
        ultra: "0.35");

    /// <summary>How strong the flashlight is on its axis: its irradiance at a metre, in the lamps' units.</summary>
    public static readonly NumericCVar<float> FlashlightIntensity = CVar.Float(
        "r_flashlight_intensity",
        18f,
        0f,
        1000f,
        CVarOptions.None,
        "Flashlight strength on its axis at one metre, in the lamps' units.");

    /// <summary>How far the flashlight reaches, in metres.</summary>
    public static readonly NumericCVar<float> FlashlightRange = CVar.Float(
        "r_flashlight_range",
        300f,
        1f,
        400f,
        CVarOptions.None,
        "How far the flashlight reaches, in metres.");

    /// <summary>How well the flashlight's beam carries to its range.</summary>
    /// <remarks>
    /// Past five metres a surface's falloff is eased by the distance raised to this: 0 keeps the inverse square,
    /// 1 leaves one over the distance, 2 holds the beam level out to its range. The inverse square alone
    /// leaves a wall at a hundred metres a five-hundredth of the near spot, which one exposure for the whole
    /// frame cannot show; an eye adapts to each part of the view and sees it lit. Captured on mission 3 at
    /// night from the hill over the base, 1 still left the compound unlit under the beam's own glow in the
    /// rain, 1.75 lit it grey, and 2 lit the tower and the walls. Only surfaces are thrown -- the beam in the
    /// air keeps the inverse square, or it hangs a column of haze down the whole range.
    /// </remarks>
    public static readonly NumericCVar<float> FlashlightThrow = CVar.Float(
        "r_flashlight_throw",
        2f,
        0f,
        2f,
        CVarOptions.None,
        "How well the flashlight carries to its range past 5 m: 0 the inverse square, 1 one over the distance, 2 level to the range.");

    /// <summary>The half-angle of the flashlight's hot core, in degrees.</summary>
    public static readonly NumericCVar<float> FlashlightInner = CVar.Float(
        "r_flashlight_inner",
        6f,
        0.5f,
        60f,
        CVarOptions.None,
        "Half-angle of the flashlight's hot core, in degrees.");

    /// <summary>The half-angle of the flashlight's main beam, in degrees.</summary>
    public static readonly NumericCVar<float> FlashlightOuter = CVar.Float(
        "r_flashlight_outer",
        16f,
        1f,
        75f,
        CVarOptions.None,
        "Half-angle of the flashlight's main beam, in degrees.");

    /// <summary>How far the flashlight's dim spill reaches, as a half-angle in degrees.</summary>
    public static readonly NumericCVar<float> FlashlightSpill = CVar.Float(
        "r_flashlight_spill",
        32f,
        1f,
        85f,
        CVarOptions.None,
        "Half-angle of the flashlight's dim spill, in degrees.");

    /// <summary>How bright the flashlight's spill is against its core.</summary>
    public static readonly NumericCVar<float> FlashlightSpillLevel = CVar.Float(
        "r_flashlight_spill_level",
        0.05f,
        0f,
        1f,
        CVarOptions.None,
        "Brightness of the flashlight's spill against its core.");

    /// <summary>The flashlight's colour temperature, in kelvin.</summary>
    /// <remarks>A modern white LED near 6000 K; an old incandescent torch near 3000 K.</remarks>
    public static readonly NumericCVar<float> FlashlightKelvin = CVar.Float(
        "r_flashlight_kelvin",
        5800f,
        2000f,
        10000f,
        CVarOptions.None,
        "Flashlight colour temperature in kelvin: 6000 a white LED, 3000 an old bulb.");

    /// <summary>The radius of the flashlight's emitter, in metres, which sets how soft its shadows are.</summary>
    public static readonly NumericCVar<float> FlashlightLens = CVar.Float(
        "r_flashlight_lens",
        0.018f,
        0f,
        0.2f,
        CVarOptions.None,
        "Radius of the flashlight's lens in metres; larger softens its shadows.");

    /// <summary>How far the flashlight's aim trails the view, as a half-life in seconds.</summary>
    public static readonly NumericCVar<float> FlashlightLag = CVar.Float(
        "r_flashlight_lag",
        0.06f,
        0f,
        0.5f,
        CVarOptions.None,
        "How far the flashlight's aim trails the view, as a half-life in seconds. 0 locks it to the view.");

    /// <summary>How much the flashlight sways as the player walks.</summary>
    public static readonly NumericCVar<float> FlashlightSway = CVar.Float(
        "r_flashlight_sway",
        1f,
        0f,
        4f,
        CVarOptions.None,
        "How much the flashlight sways as the player moves. 0 holds it still.");

    /// <summary>The thin haze only the flashlight's beam sees, per metre.</summary>
    /// <remarks>What lets the beam show indoors, where the fog's own density is nought, without fogging the room.</remarks>
    public static readonly NumericCVar<float> FlashlightDust = CVar.Float(
        "r_flashlight_dust",
        0.015f,
        0f,
        0.5f,
        CVarOptions.None,
        "Dust density only the flashlight's beam lights, per metre.");

    /// <summary>How worn the flashlight's switch is: the chance it chatters before it catches.</summary>
    /// <remarks>
    /// A worn clicky switch's contact touches and springs apart once or twice before it holds, and the light
    /// blips before it comes on. Rolled per switch-on, so it is sometimes and never the same twice; 0 is a new
    /// torch that always comes straight on, 1 one that chatters every time.
    /// </remarks>
    public static readonly NumericCVar<float> FlashlightWear = CVar.Float(
        "r_flashlight_wear",
        0.3f,
        0f,
        1f,
        CVarOptions.Archive,
        "How often the flashlight's worn switch chatters before it catches: 0 never, 1 every time.");

    /// <summary>How much of the flashlight falls back on the player's own hands and gun.</summary>
    public static readonly NumericCVar<float> FlashlightRig = CVar.Float(
        "r_flashlight_rig",
        1f,
        0f,
        4f,
        CVarOptions.None,
        "How much of the flashlight lights the player's hands and gun.");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
