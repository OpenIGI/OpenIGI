using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>The remastered thermal sensor's palettes as the eyepiece draws their scale, and their names.</summary>
/// <remarks>
/// The same stops as <c>Post/Thermal.slang</c>'s <c>Palette</c>, at the display encoding, so the scale on the eyepiece is the
/// picture's own. The shader is the authority; a change to its stops is made here too, and <c>ThermalPaletteRampTests</c> pins
/// the ends.
/// </remarks>
public static class ThermalPaletteRamp
{
    private static readonly (float R, float G, float B)[] Ironbow =
    [
        (0.00f, 0.00f, 0.04f),
        (0.12f, 0.01f, 0.30f),
        (0.40f, 0.02f, 0.52f),
        (0.72f, 0.08f, 0.40f),
        (0.90f, 0.30f, 0.10f),
        (0.98f, 0.58f, 0.02f),
        (1.00f, 0.84f, 0.30f),
        (1.00f, 0.99f, 0.86f),
    ];

    private static readonly (float R, float G, float B)[] Rainbow =
    [
        (0.00f, 0.00f, 0.10f),
        (0.05f, 0.08f, 0.55f),
        (0.00f, 0.45f, 0.85f),
        (0.00f, 0.75f, 0.45f),
        (0.55f, 0.88f, 0.05f),
        (1.00f, 0.70f, 0.00f),
        (0.95f, 0.15f, 0.05f),
        (1.00f, 0.95f, 0.92f),
    ];

    /// <summary>The colour a temperature is shown in.</summary>
    /// <param name="palette">The palette.</param>
    /// <param name="temperature">The temperature, nought for the coldest the sensor shows and one for the hottest.</param>
    /// <returns>The colour, opaque, at the display encoding.</returns>
    public static Bgra32 At(ThermalPalette palette, float temperature)
    {
        float t = Math.Clamp(temperature, 0f, 1f);
        (float r, float g, float b) = palette switch
        {
            ThermalPalette.BlackHot => Grey(MathF.Pow(1f - t, 0.9f), (0.97f, 0.99f, 1f)),
            ThermalPalette.Ironbow => Ramp(Ironbow, t),
            ThermalPalette.Rainbow => Ramp(Rainbow, t),
            _ => Lerp((0.015f, 0.02f, 0.03f), (1f, 1f, 0.985f), MathF.Pow(t, 0.9f)),
        };

        return Bgra32.FromRgb(Byte(r), Byte(g), Byte(b));
    }

    /// <summary>The palette's three-letter mark on the eyepiece.</summary>
    /// <param name="palette">The palette.</param>
    /// <returns>Its mark.</returns>
    public static string Mark(ThermalPalette palette) => palette switch
    {
        ThermalPalette.BlackHot => "BLK",
        ThermalPalette.Ironbow => "IRN",
        ThermalPalette.Rainbow => "RBW",
        _ => "WHT",
    };

    /// <summary>The palette's name, as the eyepiece announces it when it is chosen.</summary>
    /// <param name="palette">The palette.</param>
    /// <returns>Its name.</returns>
    public static string Name(ThermalPalette palette) => palette switch
    {
        ThermalPalette.BlackHot => "BLACK HOT",
        ThermalPalette.Ironbow => "IRONBOW",
        ThermalPalette.Rainbow => "RAINBOW",
        _ => "WHITE HOT",
    };

    /// <summary>The palette after this one, wrapping round.</summary>
    /// <param name="palette">The palette.</param>
    /// <returns>The next.</returns>
    public static ThermalPalette Next(ThermalPalette palette)
    {
        ThermalPalette[] all = Enum.GetValues<ThermalPalette>();
        return all[(Array.IndexOf(all, palette) + 1) % all.Length];
    }

    // As the shader's chained steps: each stop takes over across its own share of the range.
    private static (float R, float G, float B) Ramp((float R, float G, float B)[] stops, float t)
    {
        float x = t * (stops.Length - 1);
        int below = Math.Min((int)MathF.Floor(x), stops.Length - 2);
        return Lerp(stops[below], stops[below + 1], x - below);
    }

    private static (float R, float G, float B) Grey(float k, (float R, float G, float B) tint) => (k * tint.R, k * tint.G, k * tint.B);

    private static (float R, float G, float B) Lerp((float R, float G, float B) a, (float R, float G, float B) b, float t) =>
        (a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));

    private static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
}
