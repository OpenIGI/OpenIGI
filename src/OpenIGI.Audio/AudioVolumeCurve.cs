namespace OpenIGI.Audio;

/// <summary>
/// The original's gain curve — the table <c>Snd_Init</c> builds before it touches DirectSound, and the
/// thing that makes the game's volume sliders feel the way they do.
/// </summary>
/// <remarks>
/// <para>
/// <c>0x4952F0</c> fills 10001 entries at <c>0x67A4A0</c>, indexed by <c>gain × 10000</c>. Each entry is
/// <c>trunc(ln(gain) × 1442.695040888849)</c>, clamped to <c>[−10000, 0]</c>, written backwards from the
/// last entry down; entry zero is stored as the floor before the loop rather than computed.
/// </para>
/// <para>
/// <b>The unit is not decibels, and that is the interesting part.</b> The constant is
/// <c>1000 / ln(2)</c>, so the entry is <c>1000 × log2(gain)</c> — one thousand units per halving. The
/// value is then handed straight to <c>IDirectSoundBuffer::SetVolume</c>, which reads it as hundredths
/// of a decibel. A halving therefore attenuates by <b>10 dB</b> where true amplitude decibels would give
/// 6.02, so the original's curve is about <c>1.66×</c> steeper than a dB curve. Sounds fall away faster
/// than a straight port of "gain to dB" would make them, and a mix built on the arithmetically correct
/// curve is audibly wrong. The floor, −10000, is reached at a gain of <c>2^−10</c> ≈ 0.00098.
/// </para>
/// <para>
/// It matters that this is a <em>table</em> and not a formula: the original quantises the gain to one of
/// 10001 steps before taking the logarithm, so two gains differing in the fifth decimal produce exactly
/// the same attenuation. Reproducing the quantisation is what makes the vanilla profile match rather
/// than merely resemble.
/// </para>
/// <para>
/// The curve is also how a pan becomes an attenuation: the original puts <c>1 − |pan|</c> through the
/// same table and signs the result.
/// </para>
/// </remarks>
public static class AudioVolumeCurve
{
    /// <summary>The number of entries in the original's table.</summary>
    public const int EntryCount = 10001;

    /// <summary>The floor the original clamps to, and the value DirectSound treats as silence.</summary>
    public const int MinimumMillibels = -10000;

    /// <summary>Full scale.</summary>
    public const int MaximumMillibels = 0;

    /// <summary>How far the curve falls per halving of gain: one thousand units, or 10 dB as DirectSound reads them.</summary>
    public const int UnitsPerHalving = 1000;

    /// <summary>
    /// The literal at <c>0x4952F0</c>, which scales a <em>natural</em> logarithm. It is
    /// <c><see cref="UnitsPerHalving"/> / ln(2)</c>.
    /// </summary>
    private const double NaturalLogScale = 1442.695040888849;

    private static readonly int[] Table = Build();

    /// <summary>Converts a linear gain to the original's attenuation units.</summary>
    /// <param name="gain">A linear gain. Values outside zero to one are clamped.</param>
    /// <returns>The attenuation, between <see cref="MinimumMillibels"/> and zero.</returns>
    /// <remarks>
    /// The original indexes the table without a bounds check, relying on its callers to have clamped the
    /// gain to zero-to-one first. Clamping here instead is a deliberate divergence: it turns a class of
    /// caller mistake from a wild read into an inaudible one.
    /// </remarks>
    public static int ToMillibels(float gain)
    {
        if (float.IsNaN(gain) || gain <= 0f)
        {
            return MinimumMillibels;
        }

        // Truncating, as the original's cast is: this is the quantisation step that has to survive.
        int index = (int)(gain * (EntryCount - 1));
        return Table[Math.Clamp(index, 0, EntryCount - 1)];
    }

    /// <summary>Converts the original's attenuation units back to a linear gain.</summary>
    /// <param name="millibels">An attenuation in the units <see cref="ToMillibels"/> produces.</param>
    /// <returns>The equivalent linear gain, between zero and one.</returns>
    /// <remarks>
    /// Exact rather than tabulated. This exists for the enhanced profile, whose mixer wants linear
    /// amplitude, and it is what carries the original's steeper-than-decibels shape across to it.
    /// </remarks>
    public static float ToGain(int millibels) =>
        millibels <= MinimumMillibels ? 0f : (float)Math.Pow(2.0, (double)millibels / UnitsPerHalving);

    /// <summary>Puts a linear gain through the curve and back, giving the gain the original would use.</summary>
    /// <param name="gain">A linear gain.</param>
    /// <returns>The quantised gain.</returns>
    /// <remarks>This is the shortest way for a non-DirectSound backend to inherit the original's loudness.</remarks>
    public static float Quantise(float gain) => ToGain(ToMillibels(gain));

    /// <summary>Converts a pan position to the signed value the original hands to <c>SetPan</c>.</summary>
    /// <param name="pan">The pan, −1 hard left to +1 hard right. Values outside are clamped.</param>
    /// <param name="reverseStereo">Whether the profile's reverse-stereo option is on.</param>
    /// <returns>
    /// The pan in the same units, signed: DirectSound attenuates the left channel for a positive value
    /// and the right for a negative one.
    /// </returns>
    /// <remarks>
    /// Transcribed from <c>0x496E60</c>: the magnitude is the curve of <c>1 − |pan|</c> and the sign is
    /// the sign of the pan, flipped when the reverse-stereo global <c>0x543914</c> is set.
    /// </remarks>
    public static int PanToMillibels(float pan, bool reverseStereo = false)
    {
        pan = float.IsNaN(pan) ? 0f : Math.Clamp(pan, -1f, 1f);

        int sign = pan >= 0f ? 1 : -1;
        int magnitude = ToMillibels(1f - Math.Abs(pan));
        return sign * (reverseStereo ? -1 : 1) * magnitude;
    }

    private static int[] Build()
    {
        int[] table = new int[EntryCount];

        // Entry zero is silence: ln(0) is not finite, and the original writes the floor there directly.
        table[0] = MinimumMillibels;

        for (int i = 1; i < EntryCount; i++)
        {
            // Transcribed in the order the original computes it — a natural logarithm scaled by the
            // stored literal — rather than folded into log2, so the rounding matches.
            double units = Math.Log(i * 0.0001) * NaturalLogScale;
            table[i] = Math.Clamp((int)units, MinimumMillibels, MaximumMillibels);
        }

        return table;
    }
}
