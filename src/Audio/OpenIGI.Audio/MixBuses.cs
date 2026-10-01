namespace OpenIGI.Audio;

/// <summary>The four mixing buses a device levels its voices through, and the original's loudness curve.</summary>
/// <remarks>
/// <para>
/// One linear gain per <see cref="SoundCategory"/>, which is the player profile's own volume settings
/// as the host pushes them. A device that has buses of its own (a graph with a gain node per bus)
/// mirrors these onto them; one that has none (OpenAL) folds the bus gain into each voice.
/// </para>
/// <para>
/// <see cref="Level"/> is where a voice's gain becomes the original's: it goes through
/// <see cref="AudioVolumeCurve"/>, whose 10001-entry table in a thousand units per halving is
/// appreciably steeper than decibels. The quantisation is the point — it is what carries the original's
/// loudness shape onto a backend that has never heard of DirectSound.
/// </para>
/// </remarks>
public sealed class MixBuses
{
    private readonly float[] _gains = [1f, 1f, 1f, 1f];

    /// <summary>Sets the gain of one bus.</summary>
    /// <param name="category">The bus.</param>
    /// <param name="gain">The linear gain, clamped to zero to one.</param>
    public void Set(SoundCategory category, float gain) =>
        _gains[(int)category] = float.IsNaN(gain) ? 0f : Math.Clamp(gain, 0f, 1f);

    /// <summary>Gets the gain of one bus.</summary>
    /// <param name="category">The bus.</param>
    /// <returns>The linear gain.</returns>
    public float Get(SoundCategory category) => _gains[(int)category];

    /// <summary>Puts a voice gain through the original's curve.</summary>
    /// <param name="gain">The voice's own gain, zero to one.</param>
    /// <returns>The quantised linear gain, before any bus gain.</returns>
    public static float Quantise(float gain) =>
        AudioVolumeCurve.Quantise(float.IsNaN(gain) ? 0f : Math.Clamp(gain, 0f, 1f));

    /// <summary>Puts a voice gain through the original's curve, then through its bus.</summary>
    /// <param name="gain">The voice's own gain, zero to one.</param>
    /// <param name="category">The bus it plays on.</param>
    /// <returns>The linear gain to give the backend for the voice.</returns>
    public float Level(float gain, SoundCategory category) => Quantise(gain) * _gains[(int)category];
}
