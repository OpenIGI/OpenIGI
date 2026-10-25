using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;

namespace OpenIGI.Rendering;

/// <summary>The renderer's console variables for the weather: the authored particle weather and the remastered storm.</summary>
/// <remarks>One of the renderer's variable containers; <see cref="FrameCVars"/> says how they are reached.</remarks>
[CVarContainer]
public static class WeatherCVars
{
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

    /// <summary>Whether weather falls in levels the retail data never gave any.</summary>
    /// <remarks>
    /// Only three of the retail missions author a <c>RainEffect</c> task, so without this the weather
    /// can only ever be looked at in those three -- which is not enough to tune it, and not enough to
    /// find the places it goes wrong. Forcing it ignores the level's own answer and uses
    /// <see cref="WeatherStyle"/> everywhere; the level's authored fall band is replaced by the
    /// renderer's own defaults, since a level with no rain task has no band to read.
    /// </remarks>
    public static readonly BoolCVar WeatherForce = CVar.Bool(
        "r_weather_force",
        false,
        CVarOptions.Archive,
        "Make r_weather_kind fall in every level, not only the ones the retail data gives weather.");

    /// <summary>Whether the remastered storm replaces the original's rain band.</summary>
    /// <remarks>
    /// <para>
    /// Off under <c>vanilla</c>, because the original's rain is a fixed band of thin vertical streaks
    /// and that is what vanilla means here. Everywhere above it the storm is a real particle system:
    /// drops blown by a gusting wind, leaning the way they travel, sheltered by roofs and answered by
    /// splashes on the ground.
    /// </para>
    /// <para>
    /// The two never draw together. Rain that is drawn twice does not read as heavier rain, it reads
    /// as rain with a second, wrong-looking layer behind it.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar Storm = Presets.Define(
        CVar.Bool(
            "r_storm",
            true,
            CVarOptions.Archive,
            "Use the remastered particle storm instead of the original's rain band."),
        vanilla: "0",
        enhanced: "1",
        high: "1",
        ultra: "1");

    /// <summary>Whether the storm wets the ground it falls on.</summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="Storm"/> because it is a different renderer doing different work: the
    /// storm is particles in the forward pass, and this is every surface in the frame being shaded as
    /// wet — darker, glossier, and holding standing water wherever the ground is level and low. That is
    /// deferred work, so it needs the deferred path and it costs what a handful of extra instructions
    /// per shaded pixel costs.
    /// </para>
    /// <para>
    /// It is what the rain is <em>for</em>, visually. Drops falling past a bone-dry road read as an
    /// effect laid over the level; the same drops over ground that darkens, pools and throws the sky
    /// back up at the camera read as weather the level is in.
    /// </para>
    /// </remarks>
    public static readonly BoolCVar Wetness = Presets.Define(
        CVar.Bool(
            "r_wetness",
            true,
            CVarOptions.Archive,
            "Wet the ground while it rains: darker surfaces, standing water and rain ripples."),
        vanilla: "0",
        enhanced: "0",
        high: "1",
        ultra: "1");

    /// <summary>Multiplier on the storm's drop density, from a drizzle to a downpour.</summary>
    public static readonly NumericCVar<float> StormIntensity = CVar.Float(
        "r_storm_intensity",
        1f,
        0f,
        1f,
        CVarOptions.Archive,
        "How much of the storm's tuned drop rate falls: 0 is dry, 1 is the full rate.");

    /// <summary>The registry the renderer's own settings describe themselves to.</summary>
    private static GraphicsPresetRegistry Presets => GraphicsPresetRegistry.Default;
}
