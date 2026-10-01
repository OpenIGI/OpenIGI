using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>The colour a black body glows at a temperature, as a light's linear colour.</summary>
/// <remarks>
/// <para>
/// The Planckian locus in CIE 1931 chromaticity by Kim et al.'s cubic spline fit (valid 1667 K to 25000 K),
/// taken to XYZ at unit luminance and then to linear Rec. 709 -- the space the renderer lights in. The
/// result is normalised to a luminance of one, so a temperature changes a light's tint and never its
/// strength.
/// </para>
/// <para>
/// A white LED torch sits around 5500-6500 K; an old incandescent one near 3000 K, which is the yellow
/// beam of every film before the LEDs arrived.
/// </para>
/// </remarks>
public static class ColourTemperature
{
    /// <summary>Converts a colour temperature to a linear colour of luminance one.</summary>
    /// <param name="kelvin">The temperature, clamped to the fit's range.</param>
    /// <returns>The colour, every channel non-negative.</returns>
    public static Vector3f ToLinearRgb(float kelvin)
    {
        double t = Math.Clamp(kelvin, 1667f, 25000f);
        double t2 = t * t;
        double t3 = t2 * t;

        double x = t <= 4000.0
            ? (-0.2661239e9 / t3) - (0.2343589e6 / t2) + (0.8776956e3 / t) + 0.179910
            : (-3.0258469e9 / t3) + (2.1070379e6 / t2) + (0.2226347e3 / t) + 0.240390;

        double x2 = x * x;
        double x3 = x2 * x;
        double y = t <= 2222.0
            ? (-1.1063814 * x3) - (1.34811020 * x2) + (2.18555832 * x) - 0.20219683
            : t <= 4000.0
                ? (-0.9549476 * x3) - (1.37418593 * x2) + (2.09137015 * x) - 0.16748867
                : (3.0817580 * x3) - (5.87338670 * x2) + (3.75112997 * x) - 0.37001483;

        // XYZ at a luminance of one.
        double bigX = x / y;
        double bigZ = (1.0 - x - y) / y;

        double r = (3.2404542 * bigX) - (1.5371385 * 1.0) - (0.4985314 * bigZ);
        double g = (-0.9692660 * bigX) + (1.8760108 * 1.0) + (0.0415560 * bigZ);
        double b = (0.0556434 * bigX) - (0.2040259 * 1.0) + (1.0572252 * bigZ);

        r = Math.Max(r, 0.0);
        g = Math.Max(g, 0.0);
        b = Math.Max(b, 0.0);

        // Clipping a channel lost a little luminance; put it back so every temperature is equally bright.
        double luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        double scale = luminance > 1e-6 ? 1.0 / luminance : 1.0;
        return new Vector3f((float)(r * scale), (float)(g * scale), (float)(b * scale));
    }
}
