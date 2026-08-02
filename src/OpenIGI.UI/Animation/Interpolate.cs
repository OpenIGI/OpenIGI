using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Animation;

/// <summary>
/// Linear interpolation of the value types the interface animates.
/// </summary>
/// <remarks>
/// Colour blends per channel in straight (non-premultiplied, gamma-naive) space. That is not
/// physically correct, but a UI cross-fade between two similar tones is short and the error is
/// imperceptible, whereas a correct sRGB blend would cost a pair of transfer-function evaluations per
/// channel per frame for no visible gain.
/// </remarks>
public static class Interpolate
{
    /// <summary>Interpolates between two scalars.</summary>
    /// <param name="from">The value at <paramref name="t"/> = 0.</param>
    /// <param name="to">The value at <paramref name="t"/> = 1.</param>
    /// <param name="t">The blend factor.</param>
    /// <returns>The interpolated scalar.</returns>
    public static float Lerp(float from, float to, float t) => from + ((to - from) * t);

    /// <summary>Interpolates between two points.</summary>
    /// <param name="from">The point at <paramref name="t"/> = 0.</param>
    /// <param name="to">The point at <paramref name="t"/> = 1.</param>
    /// <param name="t">The blend factor.</param>
    /// <returns>The interpolated point.</returns>
    public static UiVec2 Lerp(UiVec2 from, UiVec2 to, float t) =>
        new(Lerp(from.X, to.X, t), Lerp(from.Y, to.Y, t));

    /// <summary>Interpolates between two colours, channel by channel.</summary>
    /// <param name="from">The colour at <paramref name="t"/> = 0.</param>
    /// <param name="to">The colour at <paramref name="t"/> = 1.</param>
    /// <param name="t">The blend factor.</param>
    /// <returns>The interpolated colour.</returns>
    public static Bgra32 Lerp(Bgra32 from, Bgra32 to, float t)
    {
        static byte Channel(byte a, byte b, float f) => (byte)Math.Clamp(a + ((b - a) * f), 0f, 255f);
        return new Bgra32(
            Channel(from.B, to.B, t),
            Channel(from.G, to.G, t),
            Channel(from.R, to.R, t),
            Channel(from.A, to.A, t));
    }
}
