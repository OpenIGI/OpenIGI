using System.Text.Json;

namespace OpenIGI.Rendering;

/// <summary>
/// What a mission's picture keeps of the original beyond its air: its painted sky, and how bright it is shown.
/// </summary>
/// <param name="PaintedSky">
/// How much of the level's own painted sky -- its dome and scrolling cloud layers -- is kept over the
/// atmosphere at the hour the level was painted for, nought to one. Null keeps the atmosphere alone.
/// </param>
/// <param name="ExposureStops">
/// How much brighter (positive) or darker the mission is shown than the rest, in stops. Null is none.
/// </param>
/// <param name="NightBake">
/// How much of the level's shipped lightmaps the dynamic lighting lays under its own at full night, nought
/// to one. The original's night missions are lit by those bakes under a navy dome; the moon alone leaves
/// everything it does not reach black. Null is none.
/// </param>
/// <param name="Moonlight">
/// How strongly this mission's moon lights the world against the player's <c>r_moonlight</c>, as a multiple:
/// a half halves it. Null is one.
/// </param>
/// <param name="NightSky">
/// How much of the moon this mission's night sky scatters into the shadows against <c>r_night_sky</c>, as a
/// multiple. Null is one.
/// </param>
/// <param name="NightAdaptation">
/// How far the eye may open at this mission's night against <c>r_night_adaptation</c>, as a multiple. Null is one.
/// </param>
/// <param name="NightExposureStops">
/// How much brighter (positive) or darker the mission is shown at full night, in stops, faded in with the
/// night, so a mission whose night reads too bright is darkened without touching its day. Null is none.
/// </param>
/// <param name="NightFog">
/// How much of the moon and the night sky the mission's fog catches at full night, as a multiple of its
/// day's, faded in with the night. A thin haze under a bright moon glows across the whole sky; this dims
/// that glow without touching the ground or the day's air. Null is one.
/// </param>
/// <param name="NightHalo">
/// How strongly the mission's fog throws its light forward at full night, as a multiple of its anisotropy,
/// faded in with the night. The forward lobe that gives a low sun its glow gathers a moon into a halo far
/// wider and brighter than a real moon carries through mist; a half keeps a soft one. Null is one.
/// </param>
/// <param name="NightGlow">
/// How bright the mission's night sky glows on its own at full night, as a multiple of <c>r_night_airglow</c>,
/// faded in with the night. The glow is the night's light that owes nothing to the moon, so it is what a
/// valley keeps when the moon drops behind a ridge; raised with a dimmer moon, it closes the gap between the
/// two. Null is one.
/// </param>
/// <remarks>
/// <para>
/// Read from the same mission entries as the fog in <c>fog-presets.json</c>, because they are one judgement:
/// a mission's look against what the original drew. The night's multiples scale the player's own settings
/// rather than replace them, so a mission says how its night differs and every preset keeps its say. Every field is optional, so an entry says only what
/// it changes, and the file's <c>default</c> entry stands under every mission.
/// </para>
/// <para>
/// The painted sky matters because it is most of a Project I.G.I. mission's mood -- the storm over the
/// radar base, the overcast valley, the navy night over the snow -- and the atmosphere that replaces it
/// knows only a clear sky.
/// </para>
/// </remarks>
public readonly record struct MissionLook(
    float? PaintedSky = null,
    float? ExposureStops = null,
    float? NightBake = null,
    float? Moonlight = null,
    float? NightSky = null,
    float? NightAdaptation = null,
    float? NightExposureStops = null,
    float? NightFog = null,
    float? NightHalo = null,
    float? NightGlow = null)
{
    /// <summary>Gets a look that changes nothing.</summary>
    public static MissionLook Empty { get; }

    /// <summary>Layers this look over another, taking its own value wherever it has one.</summary>
    /// <param name="fallback">The look to fall back to.</param>
    /// <returns>The combined look.</returns>
    public MissionLook Over(in MissionLook fallback) => new(
        PaintedSky ?? fallback.PaintedSky,
        ExposureStops ?? fallback.ExposureStops,
        NightBake ?? fallback.NightBake,
        Moonlight ?? fallback.Moonlight,
        NightSky ?? fallback.NightSky,
        NightAdaptation ?? fallback.NightAdaptation,
        NightExposureStops ?? fallback.NightExposureStops,
        NightFog ?? fallback.NightFog,
        NightHalo ?? fallback.NightHalo,
        NightGlow ?? fallback.NightGlow);

    /// <summary>Reads a look from a mission's JSON object.</summary>
    /// <param name="element">The object to read.</param>
    /// <returns>The look; every absent field stays null.</returns>
    public static MissionLook Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Empty;
        }

        return new MissionLook(
            Number(element, "paintedSky"),
            Number(element, "exposureStops"),
            Number(element, "nightBake"),
            Number(element, "moonlight"),
            Number(element, "nightSky"),
            Number(element, "nightAdaptation"),
            Number(element, "nightExposureStops"),
            Number(element, "nightFog"),
            Number(element, "nightHalo"),
            Number(element, "nightGlow"));
    }

    private static float? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? (float)value.GetDouble()
            : null;
}
