namespace OpenIGI.Audio;

/// <summary>The shape of a block of PCM: how many channels, how wide a sample and how fast.</summary>
/// <param name="SampleRate">The sample rate in hertz.</param>
/// <param name="Channels">The channel count, one or two.</param>
/// <param name="BitsPerSample">The sample width in bits, 8 or 16.</param>
/// <remarks>
/// The rate is carried per buffer rather than fixed for the device because the shipped sounds are not
/// all at one rate — a retail installation holds sounds at 22050, 44100 and 11025 Hz, and the original
/// simply let DirectSound resample each secondary buffer into a 22050 Hz primary.
/// </remarks>
public readonly record struct AudioFormat(int SampleRate, int Channels, int BitsPerSample = 16)
{
    /// <summary>Gets the size in bytes of one frame — one sample on every channel.</summary>
    public int BlockAlign => Channels * BitsPerSample / 8;

    /// <summary>Gets a value indicating whether the format is one the device can accept.</summary>
    public bool IsValid =>
        SampleRate > 0 && Channels is 1 or 2 && BitsPerSample is 8 or 16;

    /// <summary>Returns the number of whole frames <paramref name="byteCount"/> bytes hold.</summary>
    /// <param name="byteCount">A size in bytes.</param>
    /// <returns>The frame count.</returns>
    public long FrameCount(long byteCount) => BlockAlign == 0 ? 0 : byteCount / BlockAlign;

    /// <summary>Throws when the format is one the device cannot accept.</summary>
    /// <param name="parameterName">The name of the argument being validated.</param>
    /// <exception cref="ArgumentException">The format is not valid.</exception>
    public void Validate(string parameterName)
    {
        if (!IsValid)
        {
            throw new ArgumentException(
                $"An audio format needs a positive rate, 1 or 2 channels and 8 or 16 bits; got {this}.",
                parameterName);
        }
    }
}
