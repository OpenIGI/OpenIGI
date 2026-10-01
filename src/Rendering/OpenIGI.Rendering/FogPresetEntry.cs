using System.Text.Json;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// One mission's authored atmosphere, with every field optional.
/// </summary>
/// <remarks>
/// Nullable throughout so that a mission's entry says only what it changes. A preset file that had to
/// repeat every field per mission would be one where a change to a default silently never reached any
/// of them — which is the same reason the settings file records a diff rather than a snapshot.
/// </remarks>
public readonly record struct FogPresetEntry(
    float? Density,
    float? HeightFalloff,
    Vector3f? Albedo,
    float? Anisotropy,
    float? Distance,
    float? MultipleScattering,
    float? UniformFraction,
    float? SunIntensity,
    float? AmbientIntensity)
{
    /// <summary>Gets an entry that changes nothing.</summary>
    public static FogPresetEntry Empty { get; }

    /// <summary>Layers this entry over another, taking its own value wherever it has one.</summary>
    /// <param name="fallback">The entry to fall back to.</param>
    /// <returns>The combined entry.</returns>
    public FogPresetEntry Over(in FogPresetEntry fallback) => new(
        Density ?? fallback.Density,
        HeightFalloff ?? fallback.HeightFalloff,
        Albedo ?? fallback.Albedo,
        Anisotropy ?? fallback.Anisotropy,
        Distance ?? fallback.Distance,
        MultipleScattering ?? fallback.MultipleScattering,
        UniformFraction ?? fallback.UniformFraction,
        SunIntensity ?? fallback.SunIntensity,
        AmbientIntensity ?? fallback.AmbientIntensity);

    /// <summary>Reads an entry from its JSON object.</summary>
    /// <param name="element">The object to read.</param>
    /// <returns>The entry; every absent field stays null.</returns>
    public static FogPresetEntry Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Empty;
        }

        return new FogPresetEntry(
            Number(element, "density"),
            Number(element, "heightFalloff"),
            Colour(element, "albedo"),
            Number(element, "anisotropy"),
            Number(element, "distance"),
            Number(element, "multipleScattering"),
            Number(element, "uniformFraction"),
            Number(element, "sunIntensity"),
            Number(element, "ambientIntensity"));
    }

    private static float? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? (float)value.GetDouble()
            : null;

    private static Vector3f? Colour(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() < 3)
        {
            return null;
        }

        return new Vector3f(
            (float)value[0].GetDouble(),
            (float)value[1].GetDouble(),
            (float)value[2].GetDouble());
    }
}
