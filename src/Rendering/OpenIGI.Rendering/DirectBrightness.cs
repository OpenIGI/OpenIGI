using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>The player's brightness on the original's direct path, as screen fills that multiply the picture.</summary>
/// <remarks>
/// <para>
/// With <c>r_hdr</c> off the world is drawn straight into the back buffer and no resolve runs, and the resolve
/// is where <see cref="ExposureCVars.Exposure"/> is applied. That left the Brightness slider doing nothing at all
/// under the Original preset. Here it is applied instead as <see cref="BlendMode.Modulate2X"/> fills drawn over
/// the world before the interface, each scaling what is under it by at most two.
/// </para>
/// <para>
/// The fills scale LINEAR light by the setting, the same as the resolve does, so a value means the same on
/// either path. A fill's colour is display-encoded and the sprite shader hands it to the target as the target
/// wants it: decoded for a target that encodes as it stores, where the blend then runs in linear light, and as
/// it is for one that does not, where the blend runs on the display encoding. The colour is chosen for each.
/// </para>
/// <para>
/// Only the player's own value, not <see cref="ExposureCVars.EffectiveExposure"/>: the night and the mission's
/// look are terms of the remastered picture, and the direct path is the original's, which has neither.
/// </para>
/// </remarks>
public static class DirectBrightness
{
    /// <summary>The most fills a setting needs: four doublings reach the setting's ceiling of sixteen.</summary>
    public const int MaximumFills = 4;

    // The most one Modulate2X fill can scale by.
    private const float MostPerFill = 2f;

    // Close enough to one that a fill would only add quantisation.
    private const float Neutral = 0.005f;

    // The display encoding's exponent, for a blend that runs on encoded values.
    private const float DisplayGamma = 2.2f;

    /// <summary>Writes the fill colours that together scale the picture by a brightness.</summary>
    /// <param name="brightness">The linear scale wanted: the player's <c>r_exposure</c>.</param>
    /// <param name="targetEncodes">Whether the back buffer applies the sRGB transfer as it stores.</param>
    /// <param name="fills">Room for <see cref="MaximumFills"/> colours.</param>
    /// <returns>How many fills to draw, in order; nought where the brightness is neutral.</returns>
    /// <exception cref="ArgumentException"><paramref name="fills"/> is shorter than <see cref="MaximumFills"/>.</exception>
    public static int Fills(float brightness, bool targetEncodes, Span<Bgra32> fills)
    {
        if (fills.Length < MaximumFills)
        {
            throw new ArgumentException($"Room for {MaximumFills} fills is needed.", nameof(fills));
        }

        if (!float.IsFinite(brightness) || MathF.Abs(brightness - 1f) < Neutral)
        {
            return 0;
        }

        float remaining = Math.Clamp(brightness, 0f, MathF.Pow(MostPerFill, MaximumFills));
        int count = 0;
        while (count < MaximumFills - 1 && remaining > MostPerFill)
        {
            fills[count++] = Fill(MostPerFill, targetEncodes);
            remaining /= MostPerFill;
        }

        fills[count++] = Fill(remaining, targetEncodes);
        return count;
    }

    // The colour whose Modulate2X fill scales linear light by the factor, which is at most two.
    private static Bgra32 Fill(float factor, bool targetEncodes)
    {
        float half = Math.Clamp(factor, 0f, MostPerFill) / MostPerFill;

        // An encoding target blends decoded values, so the fill is half the factor encoded; a plain one blends
        // the encoding itself, which a linear factor reaches as its display-encoded power.
        float value = targetEncodes ? EncodeSrgb(half) : MathF.Pow(factor, 1f / DisplayGamma) / MostPerFill;
        byte level = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        return Bgra32.FromRgba(level, level, level, 255);
    }

    private static float EncodeSrgb(float linear) => linear <= 0.0031308f
        ? linear * 12.92f
        : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
}
